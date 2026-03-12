using System;
using System.IO;
using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.SystemTray;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Background service that watches for Picture-in-Picture windows and pins them
/// to all virtual desktops so they remain visible when switching desktops.
/// Detects by window title (multi-language) + browser class + topmost+small heuristic.
/// </summary>
public sealed class PipWatcher
{
    private bool _running;
    private Thread? _thread;

    // Title keywords (case-insensitive match) for all major browsers in EN + ES
    private static readonly string[] PipTitles =
    {
        "picture in picture",        // Chrome / Edge (English)
        "picture-in-picture",        // Variant with hyphen
        "imagen en imagen",          // Chrome (Spanish)
        "imagen con imagen",         // Edge (Spanish) — matches "imagen con imagen incrustada"
        "mini reproductor",          // YouTube/General Spanish
        "ventana flotante",          // Generic Spanish
        "reproductor flotante",      // Generic Spanish
        "modopip",                   // Some Spanish variant
    };

    // Browser window class prefixes that can host PiP (fallback for empty-title detection)
    private static readonly string[] BrowserClasses = { "Chrome_WidgetWin", "MozillaWindowClass", "WebView", "DeepMind" };

    // HWNDs already pinned — avoids repeated COM calls on each scan
    private readonly HashSet<nint> _pinnedHwnds = new();

    public static readonly PipWatcher Instance = new();
    private PipWatcher() { }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_TOPMOST = 0x0008;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "PipWatcher" };
        _thread.Start();
        Console.WriteLine("[PipWatcher] Started.");
    }

    public void Stop()
    {
        _running = false;
        _pinnedHwnds.Clear();
    }

    private void Loop()
    {
        while (_running)
        {
            if (ConfigManager.Instance.Config.PipWatcherEnabled)
            {
                try { CheckAndPinWindows(); }
                catch (Exception ex) { File.AppendAllText("pip_error.log", $"[{DateTime.Now}] Error: {ex.Message}\n{ex.StackTrace}\n"); }
            }
            Thread.Sleep(5000);
        }
    }

    private void CheckAndPinWindows()
    {
        var windows = new List<nint>();
        User32.EnumWindows((hwnd, _) => { windows.Add(hwnd); return true; }, 0);
        
        var vdm = VirtualDesktopManager.Instance;
        
        // Detailed logging for debugging if file exists
        bool debugEnabled = File.Exists("pip_debug_full.txt");
        if (debugEnabled) File.AppendAllText("pip_debug_full.txt", $"\n--- Scan {DateTime.Now} ---\n");

        // Stale entries cleanup
        _pinnedHwnds.RemoveWhere(h => !User32.IsWindow(h) || !User32.IsWindowVisible(h));

        foreach (var hwnd in windows)
        {
            if (_pinnedHwnds.Contains(hwnd)) continue;

            string title = GetWindowTitle(hwnd);
            string cls = GetClassName(hwnd);
            bool visible = User32.IsWindowVisible(hwnd);
            User32.GetWindowRect(hwnd, out RECT r);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            bool topmost = (exStyle & WS_EX_TOPMOST) != 0;

            if (debugEnabled && (cls.Contains("Chrome") || cls.Contains("Mozilla") || title.Contains("Picture") || title.Contains("Imagen")))
            {
                File.AppendAllText("pip_debug_full.txt", $"HWND={hwnd}, Title='{title}', Class='{cls}', Visible={visible}, Topmost={topmost}, Size={r.Width}x{r.Height}, ExStyle=0x{exStyle:X}\n");
            }

            if (!visible) continue;

            string lowerTitle = title.ToLowerInvariant();
            bool isPip = IsPipByTitle(lowerTitle) || IsBrowserPipByHeuristic(hwnd, lowerTitle);

            if (!isPip) continue;

            if (!vdm.IsWindowPinned(hwnd))
            {
                Console.WriteLine($"[PipWatcher] Pinning PiP window: '{title}' (HWND={hwnd})");
                vdm.PinWindow(hwnd);
                
                // Notify the user via Windows notification
                TrayManager.Instance?.ShowBalloon("PiP Detectado", $"La ventana '{title}' ha sido anclada a todos los escritorios.");
                
                // Debug log
                File.AppendAllText("pip_detection.log", $"[{DateTime.Now}] Detected and Pinned: '{title}' HWND={hwnd}\n");
            }
            _pinnedHwnds.Add(hwnd);
        }
    }

    private static bool IsPipByTitle(string lowerTitle)
    {
        foreach (var kw in PipTitles)
            if (lowerTitle.Contains(kw)) return true;
        return false;
    }

    /// <summary>
    /// Fallback: detect PiP windows that have an empty title but are a small,
    /// always-on-top browser window — Chrome/Edge PiP sometimes has no title text.
    /// </summary>
    private static bool IsBrowserPipByHeuristic(nint hwnd, string lowerTitle)
    {
        // Heuristic fallback: detect windows that might have a title (video name) 
        // but don't contain keywords, OR have no title at all.
        // We only proceed if it looks like a small, always-on-top browser window.

        try
        {
            // Must be a browser window class
            string cls = GetClassName(hwnd);
            bool isBrowser = false;
            foreach (var bc in BrowserClasses)
                if (cls.StartsWith(bc, StringComparison.OrdinalIgnoreCase)) { isBrowser = true; break; }
            if (!isBrowser) return false;

            // Must be a small/medium window
            User32.GetWindowRect(hwnd, out RECT r);
            int w = r.Width, h = r.Height;
            bool sizeOk = w is > 40 and < 1600 && h is > 30 and < 1200;
            
            // Must be WS_EX_TOPMOST (PiP is always-on-top)
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            
            if (isBrowser && (exStyle & WS_EX_TOPMOST) != 0 && sizeOk) return true;
            
            return false;
        }
        catch { return false; }
    }

    private static string GetWindowTitle(nint hwnd)
    {
        var buf = new char[512];
        int n = GetWindowTextW(hwnd, buf, buf.Length);
        return new string(buf, 0, n);
    }

    private static string GetClassName(nint hwnd)
    {
        var buf = new char[256];
        int n = GetClassNameW(hwnd, buf, buf.Length);
        return new string(buf, 0, n);
    }
}
