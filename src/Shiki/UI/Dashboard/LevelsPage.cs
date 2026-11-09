using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Shiki.Core;

namespace Shiki.UI.Dashboard;

/// <summary>
/// The levels as a walk: a cream cat-walk winding down the page, paw prints stamped along it
/// (bright as far as the cat has come), a numbered stop for each level with the cat sitting at
/// its level, and beside every stop what that level brings and the tasks that reach it.
/// </summary>
public sealed class LevelsPage(DashKit k, DashboardHost host)
{
    DashPalette P => k.P;

    // The walk's measurements: each level's height, the road's width, how far the stops swing.
    const double Step = 300, Road = 46, Top = 120, Bottom = 150;

    int Level => host.Level?.Invoke() ?? 1;
    CatStats Stats => host.Stats?.Invoke() ?? new CatStats(0, 0, 0, 0, 0, 0, 0, false, false);
    string Name => host.CatName();

    Color RoadFill => P.Dark ? Color.FromRgb(0xE9, 0xDF, 0xC7) : Color.FromRgb(0xF3, 0xEB, 0xD6);
    Color RoadEdge => P.Dark ? Color.FromRgb(0xB8, 0xA9, 0x86) : Color.FromRgb(0xD5, 0xC6, 0xA2);
    Color Walked => P.Dark ? Color.FromRgb(0x19, 0x71, 0xC2) : P.Accent;
    Color Ahead => Color.FromRgb(0xC3, 0xB5, 0x92);

    public UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(48, 40, 48, 56), MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(k.Text("Levels", 40, font: DashKit.Pixel));
        var intro = k.Text($"Every level is a few plain tasks. Do them, and {Name} walks on: most stops bring a new cat or something to wear. A level once reached stays reached.", 15, P.Muted, wrap: true);
        intro.Margin = new Thickness(0, 8, 0, 8);
        intro.MaxWidth = 760;
        intro.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(intro);

        var walk = new Canvas { Height = Top + (CatLevel.Max - 1) * Step + Bottom, ClipToBounds = false };
        double drawnFor = -1;
        walk.SizeChanged += (_, e) =>
        {
            // Drawn for the width it has; again only if that changes.
            if (Math.Abs(e.NewSize.Width - drawnFor) < 1) return;
            drawnFor = e.NewSize.Width;
            Draw(walk, e.NewSize.Width);
        };
        page.Children.Add(walk);
        return page;
    }

    // ---- The walk ----

    /// <summary>Where stop n sits: alternating left and right of the middle, one step lower each time.</summary>
    static Point Stop(int level, double width) =>
        new(width * (level % 2 == 1 ? 0.38 : 0.62), Top + (level - 1) * Step);

    static Point Bez(Point a, Point c1, Point c2, Point b, double t)
    {
        double u = 1 - t;
        return new Point(
            u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
            u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y);
    }

    /// <summary>The road's pieces, top to bottom: from the start, stop to stop, on to the finish.</summary>
    static List<(Point A, Point C1, Point C2, Point B, int To)> Pieces(double width)
    {
        var list = new List<(Point, Point, Point, Point, int)>();
        var start = new Point(width / 2, 0);
        var first = Stop(1, width);
        list.Add((start, new Point(start.X, Top * 0.5), new Point(first.X, Top * 0.5), first, 1));
        for (int level = 1; level < CatLevel.Max; level++)
        {
            var a = Stop(level, width);
            var b = Stop(level + 1, width);
            list.Add((a, new Point(a.X, a.Y + Step * 0.55), new Point(b.X, b.Y - Step * 0.55), b, level + 1));
        }
        var last = Stop(CatLevel.Max, width);
        var end = new Point(width / 2, last.Y + Bottom - 30);
        list.Add((last, new Point(last.X, last.Y + 70), new Point(end.X, end.Y - 60), end, CatLevel.Max + 1));
        return list;
    }

    void Draw(Canvas walk, double width)
    {
        walk.Children.Clear();
        if (width < 200) return;
        int level = Level;
        var stats = Stats;
        var pieces = Pieces(width);

        // The road: an edge, the road itself, then paw prints along its middle.
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(pieces[0].A, false, false);
            foreach (var (_, c1, c2, b, _) in pieces) c.BezierTo(c1, c2, b, true, true);
        }
        geometry.Freeze();
        walk.Children.Add(new Path { Data = geometry, Stroke = DashKit.B(RoadEdge), StrokeThickness = Road + 6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        walk.Children.Add(new Path { Data = geometry, Stroke = DashKit.B(RoadFill), StrokeThickness = Road, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        Prints(walk, pieces, level);

        // The start, and the finish.
        var start = Flag("Start", true);
        Canvas.SetLeft(start, width / 2 + Road / 2 + 12);
        Canvas.SetTop(start, 0);
        walk.Children.Add(start);
        var finish = Medal();
        var end = pieces[^1].B;
        Canvas.SetLeft(finish, end.X - 32);
        Canvas.SetTop(finish, end.Y - 20);
        walk.Children.Add(finish);

        // Each stop, and what it's about beside it.
        foreach (var goal in CatLevel.Goals)
        {
            var at = Stop(goal.Level, width);
            bool reached = goal.Level <= level, next = goal.Level == level + 1;
            var badge = StopBadge(goal.Level, reached, next);
            Canvas.SetLeft(badge, at.X - 34);
            Canvas.SetTop(badge, at.Y - 34);
            walk.Children.Add(badge);

            bool left = goal.Level % 2 == 1;
            double cardWidth = Math.Min(430, (left ? at.X : width - at.X) - 72);
            var card = Card(goal, reached, next, stats, cardWidth);
            card.Measure(new Size(cardWidth, double.PositiveInfinity));
            Canvas.SetLeft(card, left ? at.X - 72 - cardWidth : at.X + 72);
            Canvas.SetTop(card, at.Y - Math.Min(card.DesiredSize.Height / 2, 70));
            walk.Children.Add(card);

            // The cat sits on its own level's stop.
            if (goal.Level == level) walk.Children.Add(Sitting(at));
        }
    }

    /// <summary>Paw prints every so often along the road, left and right like a walk, bright up to the cat's level.</summary>
    void Prints(Canvas walk, List<(Point A, Point C1, Point C2, Point B, int To)> pieces, int level)
    {
        const double every = 40;
        double carried = every / 2;
        bool leftFoot = true;
        foreach (var (a, c1, c2, b, to) in pieces)
        {
            var colour = to <= level ? Walked : Ahead;
            var previous = a;
            for (int i = 1; i <= 160; i++)
            {
                var point = Bez(a, c1, c2, b, i / 160.0);
                double gap = (point - previous).Length;
                carried += gap;
                if (carried >= every)
                {
                    carried = 0;
                    var dir = point - previous;
                    double angle = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI + 90;
                    var side = new Vector(-dir.Y, dir.X);
                    if (side.Length > 0) side.Normalize();
                    var at = point + side * (leftFoot ? 7 : -7);
                    leftFoot = !leftFoot;
                    // Not over the stops: they sit on the road.
                    if ((point - b).Length > 44 && (point - a).Length > 44) walk.Children.Add(Paw(at, angle, colour));
                }
                previous = point;
            }
        }
    }

    static readonly Geometry PawShape = MakePaw();

    static Geometry MakePaw()
    {
        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(0, 3), 5.2, 4.4));
        group.Children.Add(new EllipseGeometry(new Point(-5.6, -3.2), 1.9, 2.4));
        group.Children.Add(new EllipseGeometry(new Point(-2, -6.4), 1.9, 2.4));
        group.Children.Add(new EllipseGeometry(new Point(2, -6.4), 1.9, 2.4));
        group.Children.Add(new EllipseGeometry(new Point(5.6, -3.2), 1.9, 2.4));
        group.Freeze();
        return group;
    }

    static Path Paw(Point at, double angle, Color colour)
    {
        var paw = new Path { Data = PawShape, Fill = DashKit.B(colour), IsHitTestVisible = false };
        paw.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1.3, 1.3), new RotateTransform(angle), new TranslateTransform(at.X, at.Y) } };
        return paw;
    }

    /// <summary>A level's stop on the road: its number, filled when reached, ringed in the cat's amber when it's next.</summary>
    Border StopBadge(int number, bool reached, bool next)
    {
        var fill = reached ? P.Accent : next ? P.Card : P.Panel;
        var edge = reached ? P.Accent : next ? DashPalette.Eye : P.Line;
        return new Border
        {
            Width = 68,
            Height = 68,
            CornerRadius = new CornerRadius(22),
            Background = DashKit.B(fill),
            BorderBrush = DashKit.B(edge),
            BorderThickness = new Thickness(next ? 4 : 3),
            Child = new TextBlock
            {
                Text = number.ToString("00"),
                FontFamily = DashKit.Pixel,
                FontSize = 26,
                Foreground = DashKit.B(reached ? P.AccentText : P.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>The cat, sitting on its stop, with a little "you are here".</summary>
    UIElement Sitting(Point stop)
    {
        var s = host.Settings;
        var stack = new StackPanel();
        var tag = new Border
        {
            Background = DashKit.B(DashPalette.Eye),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = k.Text("You are here", 12, Color.FromRgb(0x2B, 0x1D, 0x08), FontWeights.Bold),
        };
        stack.Children.Add(tag);
        var cat = new Image
        {
            Source = CatSprites.Frame(CatSprites.Dressed(CatSprites.Skin(s.Cat), s.Accessory), CatPose.Happy),
            Width = 80,
            Height = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.NearestNeighbor);
        stack.Children.Add(cat);
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(stack, stop.X - stack.DesiredSize.Width / 2);
        Canvas.SetTop(stack, stop.Y - 34 - stack.DesiredSize.Height + 8);
        return stack;
    }

    Border Flag(string text, bool start)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(k.Icon("\uE7C1", 14, P.Text));
        var label = k.Text(text, 13.5, P.Text, FontWeights.Bold);
        label.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(label);
        return new Border
        {
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(P.Line),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12, 6, 14, 7),
            Child = row,
        };
    }

    /// <summary>The end of the walk: a star for the legend.</summary>
    UIElement Medal()
    {
        bool done = Level >= CatLevel.Max;
        var stack = new StackPanel { Width = 64 };
        var star = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(32),
            Background = DashKit.B(done ? DashPalette.Eye : P.Card),
            BorderBrush = DashKit.B(DashPalette.Eye),
            BorderThickness = new Thickness(3),
            Child = k.Icon("\uE735", 26, done ? Color.FromRgb(0x2B, 0x1D, 0x08) : DashPalette.Eye),
        };
        stack.Children.Add(star);
        return stack;
    }

    // ---- What a level is about ----

    UIElement Card(LevelGoal goal, bool reached, bool next, CatStats stats, double width)
    {
        var body = new StackPanel { Width = width };
        var head = new DockPanel();
        var picture = RewardPicture(goal.Level);
        if (picture != null)
        {
            var image = new Image { Source = picture, Width = 52, Height = 52, Margin = new Thickness(12, 0, 0, 0), Opacity = reached || next ? 1 : 0.55 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            DockPanel.SetDock(image, Dock.Right);
            head.Children.Add(image);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(k.Text(goal.Title, 24, reached || next ? P.Text : P.Muted, font: DashKit.Pixel));
        titles.Children.Add(k.Text(Reward(goal.Level), 13.5, reached ? (P.Dark ? P.Accent : P.AccentEdge) : next ? DashPalette.Eye : P.Faint, FontWeights.Bold, wrap: true));
        head.Children.Add(titles);
        body.Children.Add(head);

        if (goal.Quests.Count == 0)
        {
            var start = k.Text("Where every cat starts.", 14, P.Muted, wrap: true);
            start.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(start);
        }
        foreach (var quest in goal.Quests) body.Children.Add(QuestRow(quest, stats, reached));

        if (!next) return new Border { Child = body, Opacity = reached ? 1 : 0.78, Padding = new Thickness(0, 4, 0, 0) };
        // The level being worked on: in a card ringed in amber.
        body.Width = width - 44;
        return new Border
        {
            Background = DashKit.B(P.Card),
            BorderBrush = DashKit.B(DashPalette.Eye),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20, 16, 20, 18),
            Child = body,
        };
    }

    string Reward(int level)
    {
        var unlock = CatLevel.NewAt(level).FirstOrDefault();
        var what = unlock is { IsCat: true } ? $"Brings {CatsPage.NameOf(CatSprites.Skin(unlock.Id), host.Settings)}, a new cat"
            : unlock != null ? $"Brings the {unlock.Name.ToLowerInvariant()}"
            : level == 1 ? string.Join(", ", CatLevel.Starters.Select(id => CatsPage.NameOf(CatSprites.Skin(id), host.Settings)))
            : level == CatLevel.Max ? "The legend title"
            : "";
        var state = level <= Level ? "Reached" : level == Level + 1 ? "Up next" : "";
        return state.Length == 0 ? what : what.Length == 0 ? state : $"{state} · {what}";
    }

    ImageSource? RewardPicture(int level)
    {
        var unlock = CatLevel.NewAt(level).FirstOrDefault();
        if (unlock is { IsCat: true }) return CatSprites.Head(CatSprites.Skin(unlock.Id));
        if (unlock != null) return CatSprites.Frame(CatSprites.Dressed(CatSprites.Skin(host.Settings.Cat), unlock.Id), CatPose.Idle);
        if (level == 1) return CatSprites.Head(CatSprites.Skin("mochi"));
        return null;
    }

    UIElement QuestRow(Quest quest, CatStats stats, bool reached)
    {
        bool done = reached || quest.Done(stats);
        int have = reached ? quest.Need : quest.Progress(stats);
        var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = k.Icon(done ? "\uE73E" : "\uEA3A", 14, done ? (P.Dark ? P.Accent : P.AccentEdge) : P.Faint);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 2, 0, 0);
        row.Children.Add(mark);
        var words = new StackPanel();
        words.Children.Add(k.Text(quest.Text.Replace("the cat", Name), 14, done ? P.Muted : P.Text, done ? FontWeights.Normal : FontWeights.SemiBold, wrap: true));
        if (quest.Need > 1 && !done)
        {
            var track = new Grid { Height = 5, Margin = new Thickness(0, 6, 0, 2) };
            track.Children.Add(new Border { Background = DashKit.B(DashKit.A(P.Muted, 0x40)), CornerRadius = new CornerRadius(2.5) });
            var fill = new Border { Background = DashKit.B(P.Dark ? P.Accent : P.AccentEdge), CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left };
            double part = have / (double)quest.Need;
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * part;
            track.Children.Add(fill);
            words.Children.Add(track);
        }
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var count = k.Text(quest.Need == 1 ? (done ? "Done" : "Not yet") : $"{have:N0} / {quest.Need:N0}", 12.5, P.Faint, FontWeights.SemiBold);
        count.Margin = new Thickness(12, 1, 0, 0);
        count.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(count, 2);
        row.Children.Add(count);
        return row;
    }
}
