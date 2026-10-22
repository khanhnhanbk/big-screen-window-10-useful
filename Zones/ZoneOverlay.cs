using System.Drawing.Drawing2D;

namespace WindowHelper.Zones;

/// <summary>Click-through topmost window that draws zones on one monitor.</summary>
internal sealed class ZoneOverlay : Form
{
    private static readonly Color Fill = Color.FromArgb(40, 40, 48);
    private static readonly Color Highlight = Color.FromArgb(0, 120, 215);
    private static readonly Color Border = Color.FromArgb(200, 200, 210);

    private List<Rectangle> _zones = new();
    private int _active = -1;
    private readonly Font _font = new("Segoe UI", 28f, FontStyle.Bold);

    public ZoneOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Opacity = 0.55;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                          | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>Shows the overlay over <paramref name="workArea"/>; zones are in screen coords.</summary>
    public void ShowZones(Rectangle workArea, List<Rectangle> zones, int active)
    {
        bool boundsChanged = Bounds != workArea;
        if (boundsChanged) Bounds = workArea;
        if (boundsChanged || !ReferenceEquals(zones, _zones) || active != _active)
        {
            _zones = zones;
            _active = active;
            Invalidate();
        }
        if (!Visible) Show();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Magenta);

        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            z.Offset(-Left, -Top);
            using var path = RoundedRect(z, 10);
            using var brush = new SolidBrush(i == _active ? Highlight : Fill);
            using var pen = new Pen(Border, 3);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            using var text = new SolidBrush(Color.White);
            g.DrawString((i + 1).ToString(), _font, text, z, fmt);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}
