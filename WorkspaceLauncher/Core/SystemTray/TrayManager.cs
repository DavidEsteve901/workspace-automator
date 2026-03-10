using System.Drawing;
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

    public TrayManager(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text    = "Workspace Launcher",
            Visible = true,
        };

        // Try to load icon from base directory
        string iconPath = Path.Combine(AppContext.BaseDirectory, "launcher_icon1.ico");
        if (File.Exists(iconPath))
            _notifyIcon.Icon = new Icon(iconPath);
        else
            _notifyIcon.Icon = SystemIcons.Application;

        // Context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir",  null, (_, _) => ShowWindow());
        menu.Items.Add("Lanzar último workspace", null, (_, _) => LaunchLast());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir",  null, (_, _) => ExitApp());
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
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
