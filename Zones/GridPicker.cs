using System.Drawing.Drawing2D;

namespace WindowHelper.Zones;

/// <summary>
/// gTile-style picker: Win+G shows the zone grid over the active window's monitor;
/// pick one zone or a block of zones with mouse drag or keyboard, and the window fills it.
/// Never takes focus: keys are read through the keyboard hook while it is open.
/// </summary>
internal sealed class GridPicker : Form
{
    private static readonly Color Backdrop = Color.FromArgb(20, 20, 26);
    private static readonly Color CellFill = Color.FromArgb(45, 45, 54);
    private static readonly Color CellBorder = Color.FromArgb(170, 170, 185);
    private static readonly Color Selected = Color.FromArgb(0, 120, 215);
    private const string Hint = "Click / drag to choose zones  ·  Arrows move  ·  Shift+Arrows extend  ·  Enter apply  ·  Esc cancel";

    private readonly Func<ZoneLayoutKind> _layout;
    private readonly KeyboardHook _keyboard;
    private readonly Font _numberFont = new("Segoe UI", 28f, FontStyle.Bold);
    private readonly Font _hintFont = new("Segoe UI", 12f);

    private IntPtr _target;
    private List<Rectangle> _zones = new();
    private int _cols, _rows;
    private Point _anchor, _cursor;   // (col, row)
    private bool _dragging;
    private Point _lastMouse;
    private bool _swallowGUp;

    public GridPicker(Func<ZoneLayoutKind> layout, KeyboardHook keyboard)
    {
        _layout = layout;
        _keyboard = keyboard;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Opacity = 0.8;
        BackColor = Backdrop;
        DoubleBuffered = true;

        _ = Handle; // create now so hook callbacks can BeginInvoke
        _keyboard.Filter = Filter;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    // ---- Keyboard (runs inside the hook; keep it quick) ----

    private bool Filter(int vk, bool down)
    {
        if (vk == Native.VK_G)
        {
            if (down && (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)))
            {
                _swallowGUp = true;
                BeginInvoke(Toggle);
                return true;
            }
            if (!down && _swallowGUp)
            {
                _swallowGUp = false;
                return true;
            }
        }

        if (!Visible) return false;
        if (vk is Native.VK_LEFT or Native.VK_RIGHT or Native.VK_UP or Native.VK_DOWN
            or Native.VK_RETURN or Native.VK_ESCAPE)
        {
            if (down)
            {
                bool shift = Native.IsKeyDown(Native.VK_SHIFT);
                BeginInvoke(() => HandleKey(vk, shift));
            }
            return true;
        }
        return false;
    }

    private void HandleKey(int vk, bool extend)
    {
        if (!Visible) return;
        switch (vk)
        {
            case Native.VK_ESCAPE: Finish(apply: false); return;
            case Native.VK_RETURN: Finish(apply: true); return;
        }

        var c = _cursor;
        if (vk == Native.VK_LEFT) c.X = Math.Max(0, c.X - 1);
        if (vk == Native.VK_RIGHT) c.X = Math.Min(_cols - 1, c.X + 1);
        if (vk == Native.VK_UP) c.Y = Math.Max(0, c.Y - 1);
        if (vk == Native.VK_DOWN) c.Y = Math.Min(_rows - 1, c.Y + 1);

        _cursor = c;
        if (!extend) _anchor = c;
        Invalidate();
    }

    // ---- Open / close ----

    private void Toggle()
    {
        // Stop the Win key release from opening the Start menu.
        Native.keybd_event((byte)Native.VK_UNASSIGNED, 0, 0, UIntPtr.Zero);
        Native.keybd_event((byte)Native.VK_UNASSIGNED, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);

        if (Visible)
        {
            Finish(apply: false);
            return;
        }

        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Handle || hwnd == Native.GetShellWindow()
            || hwnd == Native.GetDesktopWindow() || !Native.IsWindowVisible(hwnd))
            return;

        _target = hwnd;
        var kind = _layout();
        (_cols, _rows) = ZoneLayout.Dimensions(kind);
        var rect = Native.GetVisibleBounds(hwnd);
        var work = Screen.FromRectangle(rect).WorkingArea;
        _zones = ZoneLayout.Build(work, kind);

        // Start with the zone the window mostly sits in.
        int idx = ZoneLayout.BestMatch(_zones, rect);
        _anchor = _cursor = new Point(idx % _cols, idx / _cols);
        _dragging = false;
        _lastMouse = Cursor.Position;

        Bounds = work;
        Invalidate();
        Show();
    }

    private void Finish(bool apply)
    {
        Hide();
        _dragging = false;
        if (apply && _target != IntPtr.Zero)
            Native.MoveWindowTo(_target, SelectionRect());
        _target = IntPtr.Zero;
    }

    private Rectangle SelectionRect()
    {
        int c1 = Math.Min(_anchor.X, _cursor.X), c2 = Math.Max(_anchor.X, _cursor.X);
        int r1 = Math.Min(_anchor.Y, _cursor.Y), r2 = Math.Max(_anchor.Y, _cursor.Y);
        return Rectangle.Union(_zones[r1 * _cols + c1], _zones[r2 * _cols + c2]);
    }

    // ---- Mouse ----

    private Point? CellAt(Point client)
    {
        var screen = new Point(client.X + Left, client.Y + Top);
        for (int i = 0; i < _zones.Count; i++)
            if (_zones[i].Contains(screen)) return new Point(i % _cols, i / _cols);
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            Finish(apply: false);
            return;
        }
        if (e.Button == MouseButtons.Left && CellAt(e.Location) is { } cell)
        {
            _anchor = _cursor = cell;
            _dragging = true;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // Ignore synthetic moves (e.g. when the window appears under a still cursor).
        if (Cursor.Position == _lastMouse) return;
        _lastMouse = Cursor.Position;

        if (CellAt(e.Location) is not { } cell || cell == _cursor) return;
        _cursor = cell;
        if (!_dragging) _anchor = cell;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _dragging)
            Finish(apply: true);
    }

    // ---- Paint ----

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_zones.Count == 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var cellBrush = new SolidBrush(CellFill);
        using var borderPen = new Pen(CellBorder, 2);
        using var white = new SolidBrush(Color.White);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int i = 0; i < _zones.Count; i++)
        {
            var z = ToClient(_zones[i]);
            z.Inflate(-6, -6);
            using var path = RoundedRect(z, 10);
            g.FillPath(cellBrush, path);
            g.DrawPath(borderPen, path);
        }

        var sel = ToClient(SelectionRect());
        sel.Inflate(-6, -6);
        using (var path = RoundedRect(sel, 12))
        using (var selBrush = new SolidBrush(Color.FromArgb(200, Selected)))
        using (var selPen = new Pen(Color.White, 3))
        {
            g.FillPath(selBrush, path);
            g.DrawPath(selPen, path);
        }

        for (int i = 0; i < _zones.Count; i++)
            g.DrawString((i + 1).ToString(), _numberFont, white, ToClient(_zones[i]), fmt);

        var hintSize = g.MeasureString(Hint, _hintFont);
        var hintRect = new RectangleF((Width - hintSize.Width) / 2 - 16, 16, hintSize.Width + 32, hintSize.Height + 12);
        using (var hintBg = new SolidBrush(Color.FromArgb(230, 0, 0, 0)))
        using (var path = RoundedRect(Rectangle.Round(hintRect), 8))
            g.FillPath(hintBg, path);
        g.DrawString(Hint, _hintFont, white, hintRect, fmt);
    }

    private Rectangle ToClient(Rectangle r)
    {
        r.Offset(-Left, -Top);
        return r;
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
        if (disposing)
        {
            _keyboard.Filter = null;
            _numberFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
