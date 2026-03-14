using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.SystemTray;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.ZoneEngine;

namespace WorkspaceLauncher;

public partial class MainWindow : Window
{
    private WebBridge? _bridge;
    private TrayManager? _trayManager;
    private GlobalHookManager? _hookManager;
    private bool _forceClose = false;

    public MainWindow()
    {
        InitializeComponent();
        
        try
        {
            var iconUri = new Uri("pack://application:,,,/launcher_icon.ico");
            this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { /* Fallback to default if icon fails to load */ }

        Loaded += MainWindow_Loaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)User32.WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return nint.Zero;
    }

    private void WmGetMinMaxInfo(nint hwnd, nint lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Get the monitor that has the window
        var monitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);

        if (monitor != nint.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            User32.GetMonitorInfoW(monitor, ref monitorInfo);

            var rcWorkArea = monitorInfo.rcWork;
            var rcMonitorArea = monitorInfo.rcMonitor;

            // Normalize coordinates if needed, but usually rcWork is perfect as is
            mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
            mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
            mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Width);
            mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Height);
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load configuration
        ConfigManager.Instance.Load();

        // Sync FancyZones layouts from PowerToys into our cache
        FancyZonesReader.SyncCacheFromDisk();
        await ConfigManager.Instance.SaveAsync();

        // Trigger Virtual Desktop COM initialization
        VirtualDesktopManager.Instance.ReportStatus();

        await InitializeWebView();
        InitializeTray();
        InitializeHooks();
        PipWatcher.Instance.Start();
        ZoneAutoRegistrar.Instance.Start();
        WorkspaceLauncher.Core.CustomZoneEngine.Engine.ZoneInteractionManager.Instance.Initialize();

        // Check virtual desktop COM availability and surface any errors
        if (!VirtualDesktopManager.Instance.IsAvailable)
        {
            string error = VirtualDesktopManager.Instance.InitError ?? "Virtual desktop COM unavailable";
            Console.WriteLine($"[MainWindow] WARNING: {error}");
            // Error will be surfaced to UI once bridge is ready
            _ = Task.Delay(2000).ContinueWith(_ =>
                Dispatcher.Invoke(() => _bridge?.SendEvent("error", new { message = $"⚠ {error}" })));
        }
    }

    private async Task InitializeWebView()
    {
        // Redirect WebView2 data folder to AppData/Local to avoid cluttering the app directory
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceLauncher",
            "WebView2Cache"
        );
        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);
        
        webView.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;

        // Use the helper for standardized setup
        WebView2Helper.ApplySettings(webView.CoreWebView2);
        WebView2Helper.SetMapping(webView.CoreWebView2);

        // Hide the white flash during loading
        webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 10, 10, 10);

        _bridge = new WebBridge(webView.CoreWebView2, this);
        _bridge.Initialize();

        string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl))
        {
            Console.WriteLine($"[MainWindow] DEV MODE: Loading {devUrl}");
            webView.CoreWebView2.Navigate(devUrl);
        }
        else
        {
            webView.CoreWebView2.Navigate("https://launcher.local/index.html");
        }
    }

    private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        string error = $"[WebView2] Process failed: {e.ProcessFailedKind}. Reason: {e.Reason}";
        Logger.Error(error);

        // If it's fatal, we notify the user.
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            System.Windows.MessageBox.Show("El proceso principal de la interfaz ha fallado. La aplicación se cerrará.", 
                "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            ForceClose();
        }
        else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || 
                 e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            Logger.Warn("[WebView2] Renderer crash/unresponsive. Attempting reload...");
            webView.Reload();
        }
    }



    private void InitializeTray()
    {
        _trayManager = new TrayManager(this);
        _trayManager.Initialize();
    }

    private void InitializeHooks()
    {
        _hookManager = new GlobalHookManager();
        
        // Connect UI bridge first for telemetry/UI updates
        _hookManager.OnX1Down += (alt, ctrl, shift, win) => _bridge?.SendEvent("hotkey", new { action = "x1_down" });
        _hookManager.OnX2Down += (alt, ctrl, shift, win) => _bridge?.SendEvent("hotkey", new { action = "x2_down" });
        
        // Connect HotkeyProcessor for actual logic execution
        HotkeyProcessor.Instance.Initialize(_hookManager);

        // Centralized hotkey handling for Zone Editor
        HotkeyProcessor.Instance.OnOpenZoneEditorRequested += () => {
            Logger.Info("[MainWindow] Hotkey detected, toggling Zone Editor Manager");
            WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.ToggleManager();
        };
        
        _hookManager.Start();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            _trayManager?.ShowBalloon("Workspace Launcher", "Minimizado al área de notificación.");
        }
        else
        {
            _hookManager?.Stop();
            ZoneAutoRegistrar.Instance.Stop();
            _trayManager?.Dispose();
        }
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }
}


