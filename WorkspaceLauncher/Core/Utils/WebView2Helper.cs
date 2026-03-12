using System;
using System.IO;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.Utils;

public static class WebView2Helper
{
    public static void ApplySettings(CoreWebView2 webView)
    {
        webView.Settings.IsStatusBarEnabled = false;
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.IsZoomControlEnabled = false;
        webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
    }

    public static string GetFrontendPath()
    {
        // Check for dist folder next to the exe
        string baseDir = AppContext.BaseDirectory;
        string distPath = Path.Combine(baseDir, "frontend", "dist");
        if (Directory.Exists(distPath))
            return distPath;

        // Fallback or embedded extraction (simplified for now)
        return distPath; 
    }

    public static void SetMapping(CoreWebView2 webView)
    {
        string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl)) return;

        string path = GetFrontendPath();
        if (Directory.Exists(path))
        {
            webView.SetVirtualHostNameToFolderMapping(
                "launcher.local", path, CoreWebView2HostResourceAccessKind.Allow);
        }
    }
}
