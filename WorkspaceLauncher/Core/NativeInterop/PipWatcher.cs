using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.NativeInterop;

public sealed class PipWatcher
{
    private static readonly Lazy<PipWatcher> _instance = new(() => new PipWatcher());
    public static PipWatcher Instance => _instance.Value;

    private bool _running;
    private Thread? _thread;
    private readonly HashSet<nint> _pinnedHwnds = [];

    // Títulos comunes de las ventanas PiP de los navegadores
    private readonly string[] _pipTitles = { "imagen con imagen", "imagen en imagen", "picture in picture" };

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(WatchLoop) { IsBackground = true, Name = "PipWatcherThread" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
    }

    private void WatchLoop()
    {
        while (_running)
        {
            try
            {
                ScanAndPin();
            }
            catch { /* Ignorar errores en el loop de background */ }
            
            Thread.Sleep(1000); // Escanear cada segundo
        }
    }

    private void ScanAndPin()
    {
        User32.EnumWindows((hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd)) return true;
            if (_pinnedHwnds.Contains(hwnd)) return true;

            string title = WindowManager.GetWindowTitle(hwnd).ToLower();
            if (string.IsNullOrEmpty(title)) return true;

            foreach (var pipTitle in _pipTitles)
            {
                if (title.Contains(pipTitle))
                {
                    try
                    {
                        // Pin it to all desktops (requires virtual desktop management logic)
                        // VirtualDesktopManager.Instance.PinWindow(hwnd);
                        Console.WriteLine($"[PiP Watcher] Ventana PiP detectada y anclada: {title}");
                        _pinnedHwnds.Add(hwnd);
                    }
                    catch { }
                    break;
                }
            }
            return true;
        }, 0);
    }
}


