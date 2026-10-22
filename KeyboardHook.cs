using System.Runtime.InteropServices;

namespace WindowHelper;

/// <summary>Global low-level keyboard hook. Observes keys; <see cref="Filter"/> may swallow them.</summary>
internal sealed class KeyboardHook : IDisposable
{
    public event Action<int>? KeyDown;
    public event Action<int>? KeyUp;

    /// <summary>Called with (vk, isDown) before events; return true to swallow the key. Must be fast.</summary>
    public Func<int, bool, bool>? Filter { get; set; }

    private readonly Native.LowLevelKeyboardProc _proc; // keep delegate alive
    private IntPtr _hook;

    public KeyboardHook()
    {
        _proc = HookProc;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            int vk = (int)data.vkCode;
            bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool swallow = Filter?.Invoke(vk, isDown) ?? false;

            if (isDown)
                KeyDown?.Invoke(vk);
            else if (msg is Native.WM_KEYUP or Native.WM_SYSKEYUP)
                KeyUp?.Invoke(vk);

            if (swallow) return 1;
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
