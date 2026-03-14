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

    public static void SetMapping(CoreWebView2 webView)
    {
        string? devUrl = Environment.GetEnvironmentVariable("WL_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl)) return;

        // Serve from Embedded Resources for production/stealth mode
        webView.AddWebResourceRequestedFilter("https://launcher.local/*", CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += (sender, args) =>
        {
            try
            {
                string uri = args.Request.Uri;
                string path = uri.Replace("https://launcher.local/", "").Replace("/", ".");
                
                // Handle default route
                if (string.IsNullOrEmpty(path) || path == "index.html") path = "index.html";

                // Resource names in .NET are: AssemblyName.Folder.Folder.FileName
                // Our structure in csproj is frontend\dist\**\*
                string resourceName = $"WorkspaceLauncher.frontend.dist.{path}";
                
                // Fix for files with multiple dots or nested folders
                // We might need a better mapping if resources have complex names
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    // Fallback for SPA routing or missing resources
                    // Look for the resource by name ignoring case and common naming issues
                    string[] resources = assembly.GetManifestResourceNames();
                    string? found = resources.FirstOrDefault(r => r.EndsWith(path, StringComparison.OrdinalIgnoreCase));
                    if (found != null) stream = assembly.GetManifestResourceStream(found);
                }

                if (stream != null)
                {
                    string contentType = GetContentType(path);
                    args.Response = webView.Environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebView2] Error serving resource: {ex.Message}");
            }
        };
    }

    private static string GetContentType(string path)
    {
        if (path.EndsWith(".html")) return "text/html";
        if (path.EndsWith(".js")) return "application/javascript";
        if (path.EndsWith(".css")) return "text/css";
        if (path.EndsWith(".json")) return "application/json";
        if (path.EndsWith(".svg")) return "image/svg+xml";
        if (path.EndsWith(".png")) return "image/png";
        if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) return "image/jpeg";
        if (path.EndsWith(".ico")) return "image/x-icon";
        if (path.EndsWith(".woff")) return "font/woff";
        if (path.EndsWith(".woff2")) return "font/woff2";
        return "application/octet-stream";
    }
}


