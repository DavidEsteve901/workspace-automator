using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Background service that watches for Picture-in-Picture windows 
/// and pins them to all virtual desktops.
/// Port of _pip_watcher_loop from Python.
/// </summary>
public sealed class PipWatcher
{
    private bool _running;
    private Thread? _thread;
    private readonly string[] _pipIdentifiers = { "picture-in-picture", "picture in picture", "modopip" };

    public static readonly PipWatcher Instance = new();

    private PipWatcher() { }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);

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
    }

    private void Loop()
    {
        while (_running)
        {
            // Only run if enabled in config
            if (ConfigManager.Instance.Config.PipWatcherEnabled)
            {
                try
                {
                    CheckAndPinWindows();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PipWatcher] Error: {ex.Message}");
                }
            }

            Thread.Sleep(3500); // Check every 3.5 seconds
        }
    }

    private void CheckAndPinWindows()
    {
        var windows = WindowManager.GetVisibleWindows();
        var vdm     = VirtualDesktopManager.Instance;

        foreach (var hwnd in windows)
        {
            string title = GetWindowTitle(hwnd).ToLowerInvariant();
            if (IsPipWindow(title))
            {
                if (!vdm.IsWindowPinned(hwnd))
                {
                    Console.WriteLine($"[PipWatcher] Pinning window: {title}");
                    vdm.PinWindow(hwnd);
                }
            }
        }
    }

    private bool IsPipWindow(string title)
    {
        foreach (var id in _pipIdentifiers)
        {
            if (title.Contains(id)) return true;
        }
        return false;
    }

    private static string GetWindowTitle(nint hwnd)
    {
        int len = 512;
        var buf = new char[len];
        int actual = GetWindowTextW(hwnd, buf, len);
        return new string(buf, 0, actual);
    }
}
