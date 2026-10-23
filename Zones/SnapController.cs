namespace WindowHelper.Zones;

/// <summary>Shift+drag snapping and Win+Ctrl+Alt+Arrow hotkeys.</summary>
internal sealed class SnapController : NativeWindow, IDisposable
{
    private const int HotkeyLeft = 1, HotkeyRight = 2, HotkeyUp = 3, HotkeyDown = 4;

    private readonly Func<ZoneLayoutKind> _layout;
    private readonly Native.WinEventDelegate _winEventProc; // keep delegate alive
    private readonly IntPtr _winEventHook;
    private readonly System.Windows.Forms.Timer _dragTimer = new() { Interval = 30 };
    private readonly ZoneOverlay _overlay = new();

    private IntPtr _dragHwnd;
    private Size _dragStartSize;
    private Rectangle _dragZone = Rectangle.Empty;

    public SnapController(Func<ZoneLayoutKind> layout)
    {
        _layout = layout;
        CreateHandle(new CreateParams());

        _winEventProc = OnWinEvent;
        _winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        _dragTimer.Tick += (_, _) => UpdateDrag();

        const uint mods = Native.MOD_WIN | Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        Native.RegisterHotKey(Handle, HotkeyLeft, mods, Native.VK_LEFT);
        Native.RegisterHotKey(Handle, HotkeyRight, mods, Native.VK_RIGHT);
        Native.RegisterHotKey(Handle, HotkeyUp, mods, Native.VK_UP);
        Native.RegisterHotKey(Handle, HotkeyDown, mods, Native.VK_DOWN);
    }

    // ---- Drag snapping ----

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return; // OBJID_WINDOW only

        if (eventType == Native.EVENT_SYSTEM_MOVESIZESTART)
        {
            _dragHwnd = hwnd;
            _dragStartSize = Native.GetVisibleBounds(hwnd).Size;
            _dragZone = Rectangle.Empty;
            _dragTimer.Start();
        }
        else if (eventType == Native.EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragHwnd)
        {
            _dragTimer.Stop();
            _overlay.Hide();

            var target = _dragZone;
            var wasResize = Native.GetVisibleBounds(hwnd).Size != _dragStartSize && !Native.IsZoomed(hwnd);
            _dragHwnd = IntPtr.Zero;
            _dragZone = Rectangle.Empty;

            if (!target.IsEmpty && !wasResize)
                Native.MoveWindowTo(hwnd, target);
        }
    }

    private void UpdateDrag()
    {
        if (_dragHwnd == IntPtr.Zero) return;

        // No mouse button held: not a real mouse drag (keyboard move, or a missed MOVESIZEEND).
        // GetAsyncKeyState reads physical buttons, so check both in case they are swapped.
        if (!Native.IsKeyDown(Native.VK_LBUTTON) && !Native.IsKeyDown(Native.VK_RBUTTON))
        {
            _dragTimer.Stop();
            _overlay.Hide();
            _dragHwnd = IntPtr.Zero;
            _dragZone = Rectangle.Empty;
            return;
        }

        if (!Native.IsKeyDown(Native.VK_SHIFT))
        {
            _dragZone = Rectangle.Empty;
            if (_overlay.Visible) _overlay.Hide();
            return;
        }

        var cursor = Cursor.Position;
        var work = Screen.FromPoint(cursor).WorkingArea;
        var zones = ZoneLayout.Build(work, _layout());
        int idx = ZoneLayout.IndexAt(zones, cursor);
        _dragZone = idx >= 0 ? zones[idx] : Rectangle.Empty;
        _overlay.ShowZones(work, zones, idx);
    }

    // ---- Hotkeys ----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
            MoveForeground((int)m.WParam);
        base.WndProc(ref m);
    }

    private void MoveForeground(int hotkey)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()
            || !Native.IsWindowVisible(hwnd))
            return;

        var kind = _layout();
        var (cols, rows) = ZoneLayout.Dimensions(kind);
        var rect = Native.GetVisibleBounds(hwnd);
        var screen = Screen.FromRectangle(rect);
        var zones = ZoneLayout.Build(screen.WorkingArea, kind);
        int idx = ZoneLayout.BestMatch(zones, rect);

        // Window not snapped yet: first press just snaps it into its closest zone.
        if (!Native.IsZoomed(hwnd) && !IsSnapped(rect, zones[idx]))
        {
            Native.MoveWindowTo(hwnd, zones[idx]);
            return;
        }

        int r = idx / cols, c = idx % cols;
        switch (hotkey)
        {
            case HotkeyUp: r = Math.Max(0, r - 1); break;
            case HotkeyDown: r = Math.Min(rows - 1, r + 1); break;
            case HotkeyLeft:
                if (c > 0) c--;
                else if (Neighbor(screen, left: true) is { } ls)
                {
                    zones = ZoneLayout.Build(ls.WorkingArea, kind);
                    c = cols - 1;
                }
                break;
            case HotkeyRight:
                if (c < cols - 1) c++;
                else if (Neighbor(screen, left: false) is { } rs)
                {
                    zones = ZoneLayout.Build(rs.WorkingArea, kind);
                    c = 0;
                }
                break;
        }
        Native.MoveWindowTo(hwnd, zones[r * cols + c]);
    }

    private static bool IsSnapped(Rectangle rect, Rectangle zone) =>
        Math.Abs(rect.Left - zone.Left) <= 4 && Math.Abs(rect.Top - zone.Top) <= 4
        && Math.Abs(rect.Right - zone.Right) <= 4 && Math.Abs(rect.Bottom - zone.Bottom) <= 4;

    private static Screen? Neighbor(Screen current, bool left)
    {
        Screen? best = null;
        foreach (var s in Screen.AllScreens)
        {
            if (s.Equals(current)) continue;
            bool onSide = left ? s.Bounds.Right <= current.Bounds.Left : s.Bounds.Left >= current.Bounds.Right;
            if (!onSide) continue;
            if (best is null
                || (left ? s.Bounds.Right > best.Bounds.Right : s.Bounds.Left < best.Bounds.Left))
                best = s;
        }
        return best;
    }

    public void Dispose()
    {
        _dragTimer.Dispose();
        _overlay.Dispose();
        if (_winEventHook != IntPtr.Zero) Native.UnhookWinEvent(_winEventHook);
        for (int id = HotkeyLeft; id <= HotkeyDown; id++)
            Native.UnregisterHotKey(Handle, id);
        DestroyHandle();
    }
}
