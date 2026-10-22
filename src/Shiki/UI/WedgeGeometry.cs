using System;
using System.Windows;
using System.Windows.Media;

namespace Shiki.UI;

public static class WedgeGeometry
{
    /// <summary>
    /// A ring slice between two radii with rounded corners and straight, parallel-sided gaps
    /// to its neighbours. Angles are in radians, clockwise from 3 o'clock.
    /// </summary>
    public static Geometry Create(Point center, double innerRadius, double outerRadius,
        double startAngle, double endAngle, double gap, double corner)
    {
        if (endAngle - startAngle >= Math.PI * 2 - 1e-6)
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(center, outerRadius, outerRadius), new EllipseGeometry(center, innerRadius, innerRadius));
            ring.Freeze();
            return ring;
        }

        // Shrink by the corner radius, then widen with a round pen: that rounds every corner evenly.
        double d = gap / 2 + corner;
        double ri = innerRadius + corner, ro = outerRadius - corner;
        Point At(double radius, double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        // Offsetting by asin(d/R) keeps each side parallel to the slice's edge ray, so gaps stay even.
        double outerInset = Math.Asin(Math.Min(1, d / ro)), innerInset = Math.Asin(Math.Min(1, d / ri));

        var outerStart = At(ro, startAngle + outerInset);
        var outerEnd = At(ro, endAngle - outerInset);
        var innerEnd = At(ri, endAngle - innerInset);
        var innerStart = At(ri, startAngle + innerInset);
        bool large = endAngle - startAngle - 2 * outerInset > Math.PI;

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(ro, ro), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true));
        var core = new PathGeometry([figure]);

        var widened = core.GetWidenedPathGeometry(new Pen(Brushes.Black, corner * 2) { LineJoin = PenLineJoin.Round });
        var result = Geometry.Combine(core, widened, GeometryCombineMode.Union, null);
        result.Freeze();
        return result;
    }
}
