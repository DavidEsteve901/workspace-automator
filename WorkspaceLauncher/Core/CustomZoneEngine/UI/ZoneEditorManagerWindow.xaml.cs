using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Bridge;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.CustomZoneEngine.UI;

public partial class ZoneEditorManagerWindow : System.Windows.Window
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

        // Title bar dark/light follows the app theme setting
        bool isDark = WorkspaceLauncher.Core.Config.ConfigManager.Instance.Config.ThemeMode != "light";
        DwmHelper.UseImmersiveDarkMode(hwnd, isDark);

        // Keep System.Windows.Window background in sync with theme (avoids flash in light mode)
        var bgHex = isDark ? "#0A0A0A" : "#F0F2F5";
        Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex));

        // Ensure this control System.Windows.Window is always above the canvas and overlays
        User32.SetWindowPos(hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_SHOWWINDOW);

        // STICKY: Pin to all desktops
        VirtualDesktopManager.Instance.PinWindow(hwnd);
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
            bool _isDark = WorkspaceLauncher.Core.Config.ConfigManager.Instance.Config.ThemeMode != "light";
            webView.DefaultBackgroundColor = _isDark
                ? System.Drawing.Color.FromArgb(255, 10, 10, 10)     // #0A0A0A
                : System.Drawing.Color.FromArgb(255, 240, 242, 245);  // #F0F2F5

            _bridge = new WebBridge(webView.CoreWebView2, this); // Important: pass 'this'
            _bridge.Initialize();

            // Retry pinning after a small delay to ensure shell registration
            _ = Task.Run(async () => {
                await Task.Delay(500);
                Dispatcher.Invoke(() => {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    Logger.Info($"[ZoneEditorManagerWindow] Retrying PinWindow for HWND {hwnd}");
                    VirtualDesktopManager.Instance.PinWindow(hwnd);
                });
            });

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








