using System.Windows;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.NativeInterop;
using System.Linq;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class ZoneCanvasEditorWindow : Window
{
    private WebBridge? _bridge;
    private string _monitorHardwareId = "";
    private string _layoutId = "";
    private string _mode = "preview"; // "preview" | "edit"

    public ZoneCanvasEditorWindow(string monitorHardwareId, string layoutId = "", string mode = "preview")
    {
        _monitorHardwareId = monitorHardwareId;
        _layoutId = layoutId;
        _mode = mode;
        InitializeComponent();

        Loaded += ZoneCanvasEditorWindow_Loaded;
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) ZoneEditorLauncher.Instance.CloseAll(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        nint exStyle = User32.GetWindowLongPtr(hwnd, User32.GWL_EXSTYLE);
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, exStyle | User32.WS_EX_TOOLWINDOW);

        // Ensure we are physically above everything else, including overlays
        User32.SetWindowPos(hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
    }

    private async void ZoneCanvasEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);

            // Standardized setup
            WebView2Helper.ApplySettings(webView.CoreWebView2);
            WebView2Helper.SetMapping(webView.CoreWebView2);

            // Transparent background for canvas overlay
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            _bridge = new WebBridge(webView.CoreWebView2, this);
            _bridge.Initialize();

            string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
            string url = string.IsNullOrEmpty(devUrl) ? "https://launcher.local/index.html" : devUrl;
            string escapedId = Uri.EscapeDataString(_monitorHardwareId);
            string escapedLayout = Uri.EscapeDataString(_layoutId);
            webView.CoreWebView2.Navigate($"{url}#/zone-canvas?monitor={escapedId}&layout={escapedLayout}&mode={_mode}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneCanvasEditorWindow] Error: {ex.Message}");
        }
    }

    public void ExecuteRemoteAction(string action)
    {
        webView.CoreWebView2.PostWebMessageAsJson($"{{\"event\":\"cze_remote_action\",\"data\":{{\"action\":\"{action}\"}}}}");
    }
}
