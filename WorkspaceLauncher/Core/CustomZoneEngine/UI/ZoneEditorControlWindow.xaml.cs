using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;
using System.Text.Json;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class ZoneEditorControlWindow : Window
{
    private readonly string _monitorHardwareId;
    private readonly string _layoutId;

    public ZoneEditorControlWindow(string monitorHardwareId, string layoutId)
    {
        InitializeComponent();
        _monitorHardwareId = monitorHardwareId;
        _layoutId = layoutId;
        
        Loaded += ZoneEditorControlWindow_Loaded;
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) ZoneEditorLauncher.Instance.ToggleManager(); };
        // The closing flow is managed by ZoneEditorLauncher
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        bool isDark = WorkspaceLauncher.Core.Config.ConfigManager.Instance.Config.ThemeMode != "light";
        DwmHelper.UseImmersiveDarkMode(hwnd, isDark);

        var bgHex = isDark ? "#0A0A0A" : "#F0F2F5";
        Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex));
    }

    private async void ZoneEditorControlWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();

            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            bool isDark = WorkspaceLauncher.Core.Config.ConfigManager.Instance.Config.ThemeMode != "light";
            webView.DefaultBackgroundColor = isDark
                ? System.Drawing.Color.FromArgb(255, 10, 10, 10)
                : System.Drawing.Color.FromArgb(255, 240, 242, 245);

            // Add bridge
            var bridge = new WebBridge(webView.CoreWebView2, this);
            bridge.Initialize();
            webView.CoreWebView2.AddHostObjectToScript("bridge", bridge);

            string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
            string baseUrl = !string.IsNullOrEmpty(devUrl) ? devUrl : "https://launcher.local/index.html";
            string url = $"{baseUrl}#/zone-control?monitor={_monitorHardwareId}&layout={_layoutId}";
            webView.Source = new Uri(url);

            webView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try {
                    using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                    var root = doc.RootElement;
                    string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(action))
                        action = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    
                    if (action == "window_close")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: true);
                    }
                    else if (action == "cze_canvas_saved")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: false);
                    }
                    else if (action == "cze_canvas_discard")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: true);
                    }
                    else if (action == "cze_request_save")
                    {
                        ZoneEditorLauncher.Instance.RequestCanvasSave();
                    }
                    else if (action == "cze_request_discard")
                    {
                        ZoneEditorLauncher.Instance.RequestCanvasDiscard();
                    }
                } catch {
                    // Fallback for string messages
                    var msg = args.TryGetWebMessageAsString();
                    if (msg == "window_close")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: true);
                    }
                    else if (msg == "cze_canvas_saved")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: false);
                    }
                    else if (msg == "cze_canvas_discard")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: true);
                    }
                }
            };
        }
        catch (Exception ex)
        {
             Logger.Error($"[ZoneEditorControlWindow] Error: {ex.Message}");
        }
    }
}
