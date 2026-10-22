using System;
using System.Windows;
using System.Windows.Media;

namespace Shiki.UI;

/// <summary>
/// A shape drawn on a pixel grid, with a retro bevel: a light rim on the top-left edges and a
/// dark rim on the bottom-right ones.
/// </summary>
public sealed record PixelShape(Geometry Body, Geometry Light, Geometry Dark, Geometry Outline)
{
    /// <summary>Rasterizes everything where <paramref name="inside"/> holds, in square cells.</summary>
    public static PixelShape Create(double size, double cell, Func<double, double, bool> inside)
    {
        int n = (int)Math.Ceiling(size / cell);
        var mask = new bool[n + 2, n + 2];
        for (int row = 0; row < n; row++)
            for (int col = 0; col < n; col++)
                mask[row + 1, col + 1] = inside(col * cell + cell / 2, row * cell + cell / 2);

        bool On(int row, int col) => mask[row + 1, col + 1];
        bool Edge(int r, int c) => !On(r - 1, c) || !On(r + 1, c) || !On(r, c - 1) || !On(r, c + 1);

        return new PixelShape(
            Runs(n, cell, On),
            Runs(n, cell, (r, c) => On(r, c) && (!On(r - 1, c) || !On(r, c - 1))),
            Runs(n, cell, (r, c) => On(r, c) && (!On(r + 1, c) || !On(r, c + 1))),
            Runs(n, cell, (r, c) => On(r, c) && Edge(r, c)));
    }

    /// <summary>Merges each row's lit cells into rectangles, so the geometry stays small.</summary>
    static Geometry Runs(int n, double cell, Func<int, int, bool> lit)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int row = 0; row < n; row++)
        {
            int start = -1;
            for (int col = 0; col <= n; col++)
            {
                bool on = col < n && lit(row, col);
                if (on && start < 0) start = col;
                else if (!on && start >= 0)
                {
                    group.Children.Add(new RectangleGeometry(new Rect(start * cell, row * cell, (col - start) * cell, cell)));
                    start = -1;
                }
            }
        }
        group.Freeze();
        return group;
    }

    /// <summary>Inside test for a ring slice with straight, even gaps; corners lose one pixel so they read as rounded.</summary>
    public static Func<double, double, bool> Wedge(Point center, double inner, double outer, double start, double end, double gap, double cell)
    {
        double span = end - start;
        bool full = span >= Math.PI * 2 - 1e-6;
        return (x, y) =>
        {
            double dx = x - center.X, dy = y - center.Y, r = Math.Sqrt(dx * dx + dy * dy);
            if (r < inner || r > outer) return false;
            if (full) return true;
            double rel = Math.Atan2(dy, dx) - start;
            rel = (rel % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
            if (rel > span) return false;
            double toStart = rel < Math.PI / 2 ? r * Math.Sin(rel) : r;
            double toEnd = span - rel < Math.PI / 2 ? r * Math.Sin(span - rel) : r;
            if (toStart < gap / 2 || toEnd < gap / 2) return false;
            int near = 0;
            if (r - inner < cell) near++;
            if (outer - r < cell) near++;
            if (toStart - gap / 2 < cell) near++;
            if (toEnd - gap / 2 < cell) near++;
            return near < 2;
        };
    }

    public static Func<double, double, bool> Disc(Point center, double radius) =>
        (x, y) => (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) <= radius * radius;
}
