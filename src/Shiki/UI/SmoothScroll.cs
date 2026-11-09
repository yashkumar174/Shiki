using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiki.UI;

/// <summary>
/// Smooth scrolling for every scrolling area in the app: a turn of the mouse wheel (or a touchpad
/// swipe) glides to its place over about a fifth of a second instead of jumping. Installed once,
/// for all ScrollViewers; dragging the scrollbar or the keyboard still moves straight there.
/// </summary>
public static class SmoothScroll
{
    // How far one notch of the wheel goes, and how quickly the glide settles (higher is snappier).
    const double PixelsPerNotch = 96, Snap = 16;

    sealed class Glide
    {
        public double Target;
        public bool Running;
        public TimeSpan Last;
        public double Expected = double.NaN;
    }

    static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();
    static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrolled));
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        // The innermost area under the mouse that can scroll this way takes the turn.
        var target = Innermost(e.OriginalSource as DependencyObject, (ScrollViewer)sender, e.Delta);
        if (target == null) return;
        var glide = Glides.GetValue(target, _ => new Glide());
        if (!glide.Running) glide.Target = target.VerticalOffset;
        glide.Target = Math.Clamp(glide.Target - e.Delta / 120.0 * PixelsPerNotch, 0, target.ScrollableHeight);
        e.Handled = true;
        if (glide.Running) return;
        glide.Running = true;
        glide.Last = TimeSpan.Zero;
        void Frame(object? s, EventArgs args)
        {
            var now = ((RenderingEventArgs)args).RenderingTime;
            double dt = glide.Last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - glide.Last).TotalSeconds, 0, 0.05);
            glide.Last = now;
            if (dt == 0) return;
            double current = target.VerticalOffset;
            double gap = glide.Target - current;
            if (Math.Abs(gap) < 0.5 || !target.IsLoaded)
            {
                Stop(gap);
                return;
            }
            double next = current + gap * (1 - Math.Exp(-Snap * dt));
            glide.Expected = next;
            target.ScrollToVerticalOffset(next);
            void Stop(double rest)
            {
                if (target.IsLoaded && Math.Abs(rest) > 0) { glide.Expected = glide.Target; target.ScrollToVerticalOffset(glide.Target); }
                glide.Running = false;
                CompositionTarget.Rendering -= Frame;
            }
        }
        CompositionTarget.Rendering += Frame;
    }

    /// <summary>Something else moved the area (the scrollbar, a key, a jump to the top): the glide lets go.</summary>
    static void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || sender is not ScrollViewer viewer || !Glides.TryGetValue(viewer, out var glide) || !glide.Running) return;
        if (!double.IsNaN(glide.Expected) && Math.Abs(viewer.VerticalOffset - glide.Expected) > 2)
            glide.Target = viewer.VerticalOffset;
    }

    static ScrollViewer? Innermost(DependencyObject? from, ScrollViewer outer, int delta)
    {
        for (var node = from; node != null; node = Parent(node))
        {
            if (node is ScrollViewer viewer && CanScroll(viewer, delta)) return viewer;
            if (node == outer) break;
        }
        return null;
    }

    static bool CanScroll(ScrollViewer viewer, int delta) =>
        // Lists that scroll by whole items (CanContentScroll) keep their own scrolling.
        !viewer.CanContentScroll && viewer.ScrollableHeight > 0 && viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
        && (delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight);

    static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
