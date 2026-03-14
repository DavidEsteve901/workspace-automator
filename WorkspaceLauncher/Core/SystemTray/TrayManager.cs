using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using WorkspaceLauncher;

namespace WorkspaceLauncher.Core.SystemTray;

/// <summary>
/// System tray icon and context menu.
/// Phase 2 feature: minimize to tray.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly Window _mainWindow;
    private NotifyIcon? _notifyIcon;
    private bool _disposed;

    public static TrayManager? Instance { get; private set; }

    public TrayManager(Window mainWindow)
    {
        _mainWindow = mainWindow;
        Instance = this;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text    = "Workspace Launcher",
            Visible = true,
        };

        // Load icon: prefer embedded WPF resource (works in dev + single-file publish),
        // fall back to file on disk, then to the system default.
        _notifyIcon.Icon = LoadIcon();

        // Context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir",  null, (_, _) => ShowWindow());
        menu.Items.Add("Lanzar último workspace", null, (_, _) => LaunchLast());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir",  null, (_, _) => ExitApp());
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private static Icon LoadIcon()
    {
        // 1. Embedded WPF Resource (pack URI) — works in dev and in single-file publish
        try
        {
            var sri = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/launcher_icon.ico"));
            if (sri != null)
                return new Icon(sri.Stream);
        }
        catch { }

        // 2. File next to exe — useful if someone runs the exe directly from publish folder
        string filePath = Path.Combine(AppContext.BaseDirectory, "launcher_icon.ico");
        if (File.Exists(filePath))
            return new Icon(filePath);

        // 3. Last resort: generic Windows app icon
        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text, int timeoutMs = 2000)
    {
        _notifyIcon?.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void LaunchLast()
    {
        // Bridge to launch last category — wired up in MainWindow
        ShowWindow();
    }

    private void ExitApp()
    {
        _mainWindow.Dispatcher.Invoke(() =>
        {
            if (_mainWindow is MainWindow mw) mw.ForceClose();
            else _mainWindow.Close();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _notifyIcon?.Dispose();
        _disposed = true;
    }
}


