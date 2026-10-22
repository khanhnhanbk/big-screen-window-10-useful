namespace WindowHelper.Zones;

internal static class ZoneLayout
{
    public const int Gap = 0;

    public static (int Cols, int Rows) Dimensions(ZoneLayoutKind kind) => kind switch
    {
        ZoneLayoutKind.Grid3x2 => (3, 2),
        _ => (2, 2),
    };

    /// <summary>Zones in row-major order (index = row * cols + col).</summary>
    public static List<Rectangle> Build(Rectangle workArea, ZoneLayoutKind kind)
    {
        var (cols, rows) = Dimensions(kind);
        var zones = new List<Rectangle>(cols * rows);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int x1 = workArea.Left + workArea.Width * c / cols;
                int x2 = workArea.Left + workArea.Width * (c + 1) / cols;
                int y1 = workArea.Top + workArea.Height * r / rows;
                int y2 = workArea.Top + workArea.Height * (r + 1) / rows;
                // Full gap at screen edges, half gap between neighbors.
                int l = x1 + (c == 0 ? Gap : Gap / 2);
                int t = y1 + (r == 0 ? Gap : Gap / 2);
                int rr = x2 - (c == cols - 1 ? Gap : Gap / 2);
                int b = y2 - (r == rows - 1 ? Gap : Gap / 2);
                zones.Add(Rectangle.FromLTRB(l, t, rr, b));
            }
        }
        return zones;
    }

    public static int IndexAt(List<Rectangle> zones, Point p)
    {
        for (int i = 0; i < zones.Count; i++)
            if (zones[i].Contains(p)) return i;
        // Cursor in a gap: pick nearest zone center.
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            double dx = p.X - (z.Left + z.Width / 2.0), dy = p.Y - (z.Top + z.Height / 2.0);
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    /// <summary>Zone with the largest overlap with <paramref name="rect"/>, or nearest to its center.</summary>
    public static int BestMatch(List<Rectangle> zones, Rectangle rect)
    {
        int best = -1;
        long bestArea = 0;
        for (int i = 0; i < zones.Count; i++)
        {
            var inter = Rectangle.Intersect(zones[i], rect);
            long area = (long)inter.Width * inter.Height;
            if (area > bestArea) { bestArea = area; best = i; }
        }
        return best >= 0 ? best : IndexAt(zones, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
    }
}
