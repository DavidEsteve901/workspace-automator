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
        Closed += (s, e) => ZoneEditorLauncher.Instance.CloseAll();
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) ZoneEditorLauncher.Instance.CloseAll(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DwmHelper.UseImmersiveDarkMode(hwnd, true);
    }

    private async void ZoneEditorControlWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            
            // Add bridge
            var bridge = new WebBridge(webView.CoreWebView2, this);
            bridge.Initialize();
            webView.CoreWebView2.AddHostObjectToScript("bridge", bridge);
            
            string url = $"http://localhost:5173/#/zone-control?monitor={_monitorHardwareId}&layout={_layoutId}";
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
                        ZoneEditorLauncher.Instance.CloseAll();
                    }
                    else if (action == "cze_canvas_saved" || action == "cze_canvas_discard")
                    {
                        ZoneEditorLauncher.Instance.ReturnToAdmin();
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
                    var msg = args.TryGetWebMessageAsString();
                    if (msg == "window_close" || msg == "cze_canvas_saved" || msg == "cze_canvas_discard")
                    {
                        ZoneEditorLauncher.Instance.CloseAll();
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
