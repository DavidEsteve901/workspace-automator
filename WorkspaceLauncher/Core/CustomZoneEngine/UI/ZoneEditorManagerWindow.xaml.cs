using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class ZoneEditorManagerWindow : Window
{
    private WebBridge? _bridge;

    public ZoneEditorManagerWindow()
    {
        InitializeComponent();
        Loaded += ZoneEditorManagerWindow_Loaded;
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        
        // Ensure we use the dark title bar for Windows 11
        DwmHelper.UseImmersiveDarkMode(hwnd, true);

        // Ensure this control window is always above the canvas and overlays
        User32.SetWindowPos(hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);
    }

    private async void ZoneEditorManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            
            // Standardized setup
            WebView2Helper.ApplySettings(webView.CoreWebView2);
            WebView2Helper.SetMapping(webView.CoreWebView2);
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            _bridge = new WebBridge(webView.CoreWebView2, this); // Important: pass 'this'
            _bridge.Initialize();

            string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
            if (!string.IsNullOrEmpty(devUrl))
            {
                webView.CoreWebView2.Navigate($"{devUrl}#/zone-editor?standalone=true");
            }
            else
            {
                webView.CoreWebView2.Navigate("https://launcher.local/index.html#/zone-editor?standalone=true");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneEditorManagerWindow] Error: {ex.Message}");
        }
    }
}
