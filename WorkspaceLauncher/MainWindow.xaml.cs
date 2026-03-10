using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.SystemTray;

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
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Load configuration
        ConfigManager.Instance.Load();

        await InitializeWebView();
        InitializeTray();
        InitializeHooks();
        PipWatcher.Instance.Start();
    }

    private async Task InitializeWebView()
    {
        var env = await CoreWebView2Environment.CreateAsync();
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        _bridge = new WebBridge(webView.CoreWebView2, this);
        _bridge.Initialize();

        // Dev mode: load from Vite dev server
        string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl))
        {
            Console.WriteLine($"[MainWindow] DEV MODE: Loading {devUrl}");
            webView.CoreWebView2.Navigate(devUrl);
            return;
        }

        // Production: load embedded frontend
        string frontendPath = GetFrontendPath();
        if (frontendPath != null)
        {
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "launcher.local", frontendPath, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate("https://launcher.local/index.html");
        }
    }

    private string GetFrontendPath()
    {
        // Check for dist folder next to the exe
        string baseDir  = AppContext.BaseDirectory;
        string distPath = Path.Combine(baseDir, "frontend", "dist");
        if (Directory.Exists(distPath))
            return distPath;

        // Extract embedded resources to temp dir
        string tempPath = Path.Combine(Path.GetTempPath(), "WorkspaceLauncher_frontend");
        ExtractEmbeddedFrontend(tempPath);
        return tempPath;
    }

    private void ExtractEmbeddedFrontend(string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.Contains("frontend") || !name.Contains("dist")) continue;
            string relativePath = name
                .Replace("WorkspaceLauncher.frontend.dist.", "")
                .Replace('.', Path.DirectorySeparatorChar);
            foreach (var ext in new[] { "html", "js", "css", "json", "ico", "png", "svg", "woff", "woff2", "ttf" })
            {
                if (relativePath.EndsWith("." + ext)) break;
                if (name.EndsWith("." + ext))
                {
                    int lastDot = relativePath.LastIndexOf('.');
                    if (lastDot >= 0)
                        relativePath = relativePath[..lastDot] + "." + ext;
                    break;
                }
            }
            string fullPath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(fullPath);
            stream.CopyTo(file);
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
        _hookManager.OnX1Down += () => _bridge?.SendEvent("hotkey", new { action = "x1_down" });
        _hookManager.OnX2Down += () => _bridge?.SendEvent("hotkey", new { action = "x2_down" });
        
        // Connect HotkeyProcessor for actual logic execution
        HotkeyProcessor.Instance.Initialize(_hookManager);
        
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
            _trayManager?.Dispose();
        }
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }
}
