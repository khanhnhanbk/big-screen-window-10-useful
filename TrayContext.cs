using Microsoft.Win32;
using WindowHelper.Mouse;
using WindowHelper.Zones;

namespace WindowHelper;

internal sealed class TrayContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "WindowHelper";

    private readonly Settings _settings = Settings.Load();
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly KeyboardHook _keyboard;
    private readonly SnapController _snap;
    private readonly MouseFinder _mouseFinder;
    private readonly GridPicker _picker;

    public TrayContext()
    {
        _keyboard = new KeyboardHook();
        _snap = new SnapController(() => _settings.Layout);
        _picker = new GridPicker(() => _settings.Layout, _keyboard);
        _mouseFinder = new MouseFinder(_keyboard) { Enabled = _settings.FindMouseEnabled };

        _icon = CreateIcon();
        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "WindowHelper",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var grid2x2 = new ToolStripMenuItem("Layout 2x2");
        var grid3x2 = new ToolStripMenuItem("Layout 3x2");
        void SyncLayout()
        {
            grid2x2.Checked = _settings.Layout == ZoneLayoutKind.Grid2x2;
            grid3x2.Checked = _settings.Layout == ZoneLayoutKind.Grid3x2;
        }
        void SetLayout(ZoneLayoutKind kind)
        {
            _settings.Layout = kind;
            _settings.Save();
            SyncLayout();
        }
        grid2x2.Click += (_, _) => SetLayout(ZoneLayoutKind.Grid2x2);
        grid3x2.Click += (_, _) => SetLayout(ZoneLayoutKind.Grid3x2);
        SyncLayout();

        var findMouse = new ToolStripMenuItem("Find My Mouse (double Ctrl)") { Checked = _settings.FindMouseEnabled };
        findMouse.Click += (_, _) =>
        {
            _settings.FindMouseEnabled = !_settings.FindMouseEnabled;
            _mouseFinder.Enabled = _settings.FindMouseEnabled;
            findMouse.Checked = _settings.FindMouseEnabled;
            _settings.Save();
        };

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) =>
        {
            SetStartup(!startup.Checked);
            startup.Checked = IsStartupEnabled();
        };

        var help = new ToolStripMenuItem("Shift+drag: snap  |  Win+G: pick zones  |  Win+Ctrl+Alt+Arrows: move") { Enabled = false };

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();

        menu.Items.AddRange([grid2x2, grid3x2, new ToolStripSeparator(), findMouse, startup,
            new ToolStripSeparator(), help, new ToolStripSeparator(), exit]);
        return menu;
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    /// <summary>Draws a small 2x2 grid icon so no .ico file is needed.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillRectangle(brush, 2, 2, 13, 13);
            g.FillRectangle(brush, 17, 2, 13, 13);
            g.FillRectangle(brush, 2, 17, 13, 13);
            g.FillRectangle(Brushes.White, 17, 17, 13, 13);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _mouseFinder.Dispose();
        _picker.Dispose();
        _snap.Dispose();
        _keyboard.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
