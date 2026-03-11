using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;

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
        "modopip",                   // Some Spanish variant
    };

    // Browser window class prefixes that can host PiP (fallback for empty-title detection)
    private static readonly string[] BrowserClasses = { "Chrome_WidgetWin", "MozillaWindowClass" };

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
                catch (Exception ex) { Console.WriteLine($"[PipWatcher] Error: {ex.Message}"); }
            }
            Thread.Sleep(2000);
        }
    }

    private void CheckAndPinWindows()
    {
        var windows = WindowManager.GetVisibleWindows();
        var vdm     = VirtualDesktopManager.Instance;

        // Cleanup stale entries: window closed or no longer a PiP
        _pinnedHwnds.RemoveWhere(h =>
            !User32.IsWindow(h) ||
            !User32.IsWindowVisible(h) ||
            !IsPipByTitle(GetWindowTitle(h).ToLowerInvariant()));

        foreach (var hwnd in windows)
        {
            if (_pinnedHwnds.Contains(hwnd)) continue;

            string title = GetWindowTitle(hwnd).ToLowerInvariant();
            bool isPip = IsPipByTitle(title) || IsBrowserPipByHeuristic(hwnd, title);

            if (!isPip) continue;

            if (!vdm.IsWindowPinned(hwnd))
            {
                Console.WriteLine($"[PipWatcher] Pinning PiP window: '{title}' (HWND={hwnd})");
                vdm.PinWindow(hwnd);
            }
            _pinnedHwnds.Add(hwnd); // Don't check again even if PinWindow failed (avoid spam)
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
        if (!string.IsNullOrEmpty(lowerTitle)) return false; // Title-based check handles it

        try
        {
            // Must be a browser window class
            string cls = GetClassName(hwnd);
            bool isBrowser = false;
            foreach (var bc in BrowserClasses)
                if (cls.StartsWith(bc, StringComparison.OrdinalIgnoreCase)) { isBrowser = true; break; }
            if (!isBrowser) return false;

            // Must be WS_EX_TOPMOST (PiP is always-on-top)
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOPMOST) == 0) return false;

            // Must be a small window (PiP is typically < 800×600, > 100×60)
            User32.GetWindowRect(hwnd, out RECT r);
            int w = r.Width, h = r.Height;
            return w is > 60 and < 900 && h is > 40 and < 700;
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
