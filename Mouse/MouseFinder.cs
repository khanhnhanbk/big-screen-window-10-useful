namespace WindowHelper.Mouse;

/// <summary>Double-tap Ctrl to spotlight the mouse cursor.</summary>
internal sealed class MouseFinder : IDisposable
{
    private const int DoubleTapMs = 400;
    private const int MaxTapMs = 300;

    private readonly KeyboardHook _hook;
    private readonly SpotlightForm _spotlight = new();
    private readonly SynchronizationContext _ui;

    private bool _ctrlDown;
    private bool _dirty;          // another key was pressed while Ctrl was held
    private long _ctrlDownAt;
    private long _lastTapUpAt = -10_000;
    private long _pressStartAfterTap = -10_000;

    public bool Enabled { get; set; } = true;

    public MouseFinder(KeyboardHook hook)
    {
        _hook = hook;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
    }

    private static bool IsCtrl(int vk) => vk is Native.VK_LCONTROL or Native.VK_RCONTROL or 0x11;

    private void OnKeyDown(int vk)
    {
        long now = Environment.TickCount64;
        if (IsCtrl(vk))
        {
            if (_ctrlDown) return; // auto-repeat
            _ctrlDown = true;
            _dirty = false;
            _ctrlDownAt = now;
            _pressStartAfterTap = now - _lastTapUpAt <= DoubleTapMs ? now : -10_000;
            return;
        }

        _dirty = true;
        _lastTapUpAt = -10_000;
        if (_spotlight.Visible) _ui.Post(_ => _spotlight.Dismiss(), null);
    }

    private void OnKeyUp(int vk)
    {
        if (!IsCtrl(vk) || !_ctrlDown) return;
        _ctrlDown = false;

        long now = Environment.TickCount64;
        bool cleanTap = !_dirty && now - _ctrlDownAt <= MaxTapMs;
        if (!cleanTap)
        {
            _lastTapUpAt = -10_000;
            return;
        }

        if (_pressStartAfterTap == _ctrlDownAt)
        {
            _lastTapUpAt = -10_000; // consume; a third tap starts fresh
            if (Enabled) _ui.Post(_ => _spotlight.Spot(), null);
        }
        else
        {
            _lastTapUpAt = now;
        }
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        _spotlight.Dispose();
    }
}

/// <summary>
/// Full virtual-screen dim layer with a yellow spotlight that follows the cursor.
/// Renders into a per-pixel-alpha bitmap and pushes only the changed area each frame
/// (UpdateLayeredWindowIndirect), paced to the display refresh with DwmFlush.
/// </summary>
internal sealed class SpotlightForm : Form
{
    private const int StartRadius = 600;
    private const int EndRadius = 90;
    private const int RingWidth = 5;
    private const int ShrinkMs = 350;
    private const int LifetimeMs = 1500;
    private const int FadeMs = 250;

    // Premultiplied BGRA (0xAARRGGBB with RGB already scaled by A).
    private static readonly uint Dim = Premultiply(150, 0, 0, 0);
    private static readonly uint Tint = Premultiply(55, 255, 214, 0);
    private static readonly uint Ring = Premultiply(255, 255, 214, 0);

    private IntPtr _memDc, _dib, _oldBitmap;
    private unsafe uint* _bits;
    private Size _surface;

    private Thread? _frameThread;
    private volatile bool _running;
    private int _framePending;

    private long _startedAt;
    private Rectangle _lastDirty = Rectangle.Empty;

    public SpotlightForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        _ = Handle;
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

    // Layered windows updated via UpdateLayeredWindow never paint through WM_PAINT.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    public unsafe void Spot()
    {
        var vs = SystemInformation.VirtualScreen;
        EnsureSurface(vs.Size);
        new Span<uint>(_bits, _surface.Width * _surface.Height).Fill(Dim);
        _lastDirty = Rectangle.Empty;
        _startedAt = Environment.TickCount64;

        Bounds = vs;
        RenderFrame(fullUpdate: true);
        if (!Visible) Show();
        StartFrameLoop();
    }

    public void Dismiss()
    {
        _running = false;
        Hide();
        FreeSurface(); // ~14 MB at 1440p; recreated on next Spot()
    }

    // ---- Frame pacing ----

    private void StartFrameLoop()
    {
        if (_running) return;
        _running = true;
        _frameThread = new Thread(() =>
        {
            while (_running)
            {
                if (Native.DwmFlush() != 0) Thread.Sleep(8); // wait for next vsync
                if (Interlocked.Exchange(ref _framePending, 1) == 0)
                {
                    try { BeginInvoke(OnFrame); }
                    catch (InvalidOperationException) { return; } // handle destroyed on exit
                }
            }
        }) { IsBackground = true, Name = "Spotlight frames" };
        _frameThread.Start();
    }

    private void OnFrame()
    {
        Interlocked.Exchange(ref _framePending, 0);
        if (_running) RenderFrame(fullUpdate: false);
    }

    private void RenderFrame(bool fullUpdate)
    {
        long elapsed = Environment.TickCount64 - _startedAt;
        if (elapsed >= LifetimeMs + FadeMs || Native.IsKeyDown(0x01) || Native.IsKeyDown(0x02))
        {
            Dismiss();
            return;
        }

        double t = Math.Min(1.0, elapsed / (double)ShrinkMs);
        double ease = 1 - Math.Pow(1 - t, 3);
        int radius = (int)Math.Round(StartRadius + (EndRadius - StartRadius) * ease);
        double fade = elapsed > LifetimeMs ? 1 - (elapsed - LifetimeMs) / (double)FadeMs : 1;

        var c = Cursor.Position;
        c.Offset(-Left, -Top);

        var spot = new Rectangle(c.X - radius - 2, c.Y - radius - 2, radius * 2 + 4, radius * 2 + 4);
        var dirty = _lastDirty.IsEmpty ? spot : Rectangle.Union(_lastDirty, spot);
        dirty.Intersect(new Rectangle(Point.Empty, _surface));
        _lastDirty = spot;

        if (!dirty.IsEmpty) DrawSpot(c, radius, dirty);
        Push((byte)Math.Clamp(fade * 255, 0, 255), fullUpdate ? Rectangle.Empty : dirty);
    }

    // ---- Rendering ----

    private unsafe void DrawSpot(Point c, int radius, Rectangle dirty)
    {
        float outer = radius;                 // ring outer edge; dim beyond
        float inner = radius - RingWidth;     // ring inner edge; tint inside
        float outerSq = outer * outer;
        float innerSafeSq = (inner - 1) * (inner - 1);
        int stride = _surface.Width;

        for (int y = dirty.Top; y < dirty.Bottom; y++)
        {
            uint* row = _bits + y * stride;
            float dy = y + 0.5f - c.Y;
            float dySq = dy * dy;
            for (int x = dirty.Left; x < dirty.Right; x++)
            {
                float dx = x + 0.5f - c.X;
                float dSq = dx * dx + dySq;
                if (dSq >= outerSq) { row[x] = Dim; continue; }
                if (dSq <= innerSafeSq) { row[x] = Tint; continue; }

                float d = MathF.Sqrt(dSq);
                if (d < inner) row[x] = Lerp(Tint, Ring, d - (inner - 1));
                else if (d <= outer - 1) row[x] = Ring;
                else row[x] = Lerp(Ring, Dim, d - (outer - 1));
            }
        }
    }

    private unsafe void Push(byte alpha, Rectangle dirty)
    {
        var dst = new Native.POINT { X = Left, Y = Top };
        var size = new Native.SIZE { cx = _surface.Width, cy = _surface.Height };
        var src = new Native.POINT();
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            SourceConstantAlpha = alpha,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };
        var dirtyRect = new Native.RECT { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };

        var info = new Native.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(Native.UPDATELAYEREDWINDOWINFO),
            pptDst = &dst,
            psize = &size,
            hdcSrc = _memDc,
            pptSrc = &src,
            pblend = &blend,
            dwFlags = Native.ULW_ALPHA,
            prcDirty = dirty.IsEmpty ? null : &dirtyRect,
        };
        Native.UpdateLayeredWindowIndirect(Handle, &info);
    }

    private unsafe void EnsureSurface(Size size)
    {
        if (_dib != IntPtr.Zero && size == _surface) return;
        FreeSurface();

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
            biWidth = size.Width,
            biHeight = -size.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(screenDc);
        _dib = Native.CreateDIBSection(screenDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        Native.ReleaseDC(IntPtr.Zero, screenDc);
        _oldBitmap = Native.SelectObject(_memDc, _dib);
        _bits = (uint*)bits;
        _surface = size;
    }

    private unsafe void FreeSurface()
    {
        if (_memDc != IntPtr.Zero)
        {
            Native.SelectObject(_memDc, _oldBitmap);
            Native.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            Native.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        _bits = null;
    }

    private static uint Premultiply(byte a, byte r, byte g, byte b) =>
        (uint)a << 24 | (uint)(r * a / 255) << 16 | (uint)(g * a / 255) << 8 | (uint)(b * a / 255);

    /// <summary>Per-channel blend of two premultiplied colors; t in [0,1].</summary>
    private static uint Lerp(uint from, uint to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            result |= (uint)(a + (b - a) * t + 0.5f) << shift;
        }
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        _running = false;
        FreeSurface();
        base.Dispose(disposing);
    }
}
