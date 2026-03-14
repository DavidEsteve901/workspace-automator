using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.ZoneEngine;

/// <summary>
/// Listens for EVENT_SYSTEM_MOVESIZEEND via a WinEvent hook.
/// When a window is manually moved/resized to a FancyZones zone position,
/// it is automatically registered in the zone stack so it can be cycled.
///
/// This replaces the need to only use the launcher to track windows —
/// any window dragged into a zone (e.g. via FancyZones Shift+drag) gets
/// added to that zone's group automatically.
/// </summary>
public sealed class ZoneAutoRegistrar : IDisposable
{
    public static readonly ZoneAutoRegistrar Instance = new();
    private ZoneAutoRegistrar() { }

    private nint  _hook;
    private Thread? _thread;
    private uint  _threadId;
    private bool  _running;
    private bool  _disposed;

    // Keep delegate alive to prevent GC collection
    private User32.WinEventProc? _procRef;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(RunMessagePump) { IsBackground = true, Name = "ZoneAutoRegistrar" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_threadId != 0)
            User32.PostThreadMessageW(_threadId, User32.WM_QUIT, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void RunMessagePump()
    {
        _threadId = Kernel32.GetCurrentThreadId();
        _procRef  = OnWinEvent; // Keep alive

        // Subscribe to window move/resize end events (all processes, all threads)
        _hook = User32.SetWinEventHook(
            User32.EVENT_SYSTEM_MOVESIZEEND,
            User32.EVENT_SYSTEM_MOVESIZEEND,
            nint.Zero,
            _procRef,
            0, 0,
            User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);

        if (_hook == 0)
        {
            Console.WriteLine("[ZoneAutoRegistrar] Failed to install WinEvent hook.");
            _running = false;
            return;
        }

        Console.WriteLine("[ZoneAutoRegistrar] WinEvent hook installed.");

        // Message pump required for WINEVENT_OUTOFCONTEXT callbacks
        while (_running)
        {
            int ret = User32.GetMessageW(out var msg, 0, 0, 0);
            if (ret <= 0) break;
            User32.TranslateMessage(ref msg);
            User32.DispatchMessageW(ref msg);
        }

        if (_hook != 0) User32.UnhookWinEvent(_hook);
        Console.WriteLine("[ZoneAutoRegistrar] WinEvent hook removed.");
    }

    private static void OnWinEvent(nint hHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwThread, uint dwTime)
    {
        // Only care about top-level window moves (idObject == 0 = OBJID_WINDOW)
        if (hwnd == 0 || idObject != 0) return;
        if (!User32.IsWindowVisible(hwnd)) return;

        // Check if this window landed on a zone position
        // Run on a thread pool thread to avoid blocking the hook callback
        Task.Run(() => TryRegisterWindowInZone(hwnd));
    }

    private static void TryRegisterWindowInZone(nint hwnd)
    {
        try
        {
            // Minimal delay so FancyZones has time to finish snapping
            Thread.Sleep(50);

            // Detect the zone the window is now sitting at
            var newKey = ZoneCycler.DetectZoneByPosition(hwnd);
            if (newKey == null) return; // Not at any zone position — ignore

            // Check current registration
            var oldKey = ZoneStack.Instance.FindKeyForHwnd(hwnd);

            // Already in the correct zone — nothing to do
            if (oldKey != null && oldKey == newKey) return;

            // Moved from one zone to another (or newly placed) — update registration
            if (oldKey != null)
            {
                ZoneStack.Instance.Unregister(hwnd);
                Logger.Info($"[ZoneAutoRegistrar] Unregistered hwnd={hwnd} from old zone {oldKey.Zone}");
            }

            ZoneStack.Instance.Register(newKey, hwnd);
            Logger.Success($"[ZoneAutoRegistrar] Registered hwnd={hwnd} → zone {newKey.Zone} layout={newKey.Layout[..8]}...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZoneAutoRegistrar] Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}


