using System.Windows;
using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;
using WorkspaceLauncher.Core.CustomZoneEngine.UI;
using WorkspaceLauncher.Core.CustomZoneEngine.Adapters;
using WorkspaceLauncher.Core.ZoneEngine;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Engine;

/// <summary>
/// Monitors window move/resize events and handles 'Shift' based snapping.
/// </summary>
public sealed class ZoneInteractionManager : IDisposable
{
    public static readonly ZoneInteractionManager Instance = new();
    private ZoneInteractionManager() { }

    private nint _hook;
    private User32.WinEventProc? _procRef;
    private bool _isDragging;
    private nint _draggingHwnd;
    private bool _disposed;

    public void Initialize()
    {
        _procRef = OnWinEvent;
        _hook = User32.SetWinEventHook(
            User32.EVENT_SYSTEM_MOVESIZESTART,
            User32.EVENT_SYSTEM_MOVESIZEEND,
            nint.Zero,
            _procRef,
            0, 0,
            User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0) return; // Only top-level windows

        if (eventType == User32.EVENT_SYSTEM_MOVESIZESTART)
        {
            _isDragging = true;
            _draggingHwnd = hwnd;
            Task.Run(MonitorDrag);
        }
        else if (eventType == User32.EVENT_SYSTEM_MOVESIZEEND)
        {
            if (_isDragging && _draggingHwnd != nint.Zero)
            {
                bool shiftPressed = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
                if (shiftPressed)
                {
                    nint targetHwnd = _draggingHwnd;
                    Task.Run(() => TrySnapWindow(targetHwnd));
                }
            }
            _isDragging = false;
            _draggingHwnd = nint.Zero;
        }
    }

    private void TrySnapWindow(nint hwnd)
    {
        try
        {
            User32.GetCursorPos(out POINT pt);
            nint hMonitor = User32.MonitorFromPoint(pt, User32.MONITOR_DEFAULTTONEAREST);
            
            var monitors = Utils.MonitorManager.GetActiveMonitors();
            var monitor = monitors.FirstOrDefault(m => m.Handle == hMonitor.ToString());
            if (monitor == null)
            {
                Logger.Warn($"[ZoneInteractionManager] TrySnapWindow: Monitor not found for handle {hMonitor}");
                return;
            }

            var desktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
            if (!desktopId.HasValue) 
            {
                Logger.Warn("[ZoneInteractionManager] TrySnapWindow: Could not get current desktop ID");
                return;
            }

            var engine = ZoneEngineManager.Current;
            
            var monitorInfo = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            User32.GetMonitorInfoW(hMonitor, ref monitorInfo);

            Logger.Info($"[ZoneInteractionManager] TrySnapWindow: Searching zone at ({pt.X}, {pt.Y}) for monitor {monitor.PtName}");
            var hit = engine.DetectZoneAtPoint(pt.X, pt.Y, monitor.PtInstance, desktopId.Value, monitorInfo.rcWork);
            
            if (hit.HasValue)
            {
                // Minimal delay to let the OS settle after mouse release
                Thread.Sleep(10);

                Logger.Info($"[ZoneInteractionManager] TrySnapWindow: Zone hit! Layout={hit.Value.LayoutId}, Index={hit.Value.ZoneIndex}");
                var rect = engine.CalculateZoneRect(hit.Value.LayoutId, hit.Value.ZoneIndex, monitorInfo.rcWork);
                if (rect.HasValue)
                {
                    Logger.Info($"[ZoneInteractionManager] TrySnapWindow: Snapping HWND {hwnd} to {rect.Value.Left},{rect.Value.Top} {rect.Value.Width}x{rect.Value.Height}");
                    bool snapped = Win32ZoneArranger.Instance.SnapWindow(hwnd, rect.Value);
                    
                    if (snapped)
                    {
                        // Register in ZoneStack for cycling compatibility
                        var key = new ZoneStack.ZoneKey(desktopId.Value, monitor.PtInstance, hit.Value.LayoutId, hit.Value.ZoneIndex);
                        ZoneStack.Instance.Register(key, hwnd);
                        Logger.Success($"[ZoneInteractionManager] Window snapped successfully");
                    }
                    else
                    {
                        Logger.Error("[ZoneInteractionManager] Win32ZoneArranger failed to snap window");
                    }
                }
                else
                {
                    Logger.Warn("[ZoneInteractionManager] TrySnapWindow: Could not calculate zone rect");
                }
            }
            else
            {
                Logger.Info("[ZoneInteractionManager] TrySnapWindow: No zone hit at this point");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ZoneInteractionManager] TrySnapWindow error: {ex.Message}");
        }
    }

    private OverlayWindow? _overlay;
    private nint _lastHMonitor;
    private string? _lastLayoutId;

    private async Task MonitorDrag()
    {
        _lastHMonitor = nint.Zero;
        _lastLayoutId = null;

        while (_isDragging && _draggingHwnd != nint.Zero)
        {
            bool shiftPressed = (User32.GetAsyncKeyState(User32.VK_SHIFT) & 0x8000) != 0;
            if (shiftPressed)
            {
                UpdateOverlay();
            }
            else
            {
                HideOverlay();
                _lastHMonitor = nint.Zero;
                _lastLayoutId = null;
            }

            await Task.Delay(30);
        }
        HideOverlay();
    }

    private void UpdateOverlay()
    {
        User32.GetCursorPos(out POINT pt);
        nint hMonitor = User32.MonitorFromPoint(pt, User32.MONITOR_DEFAULTTONEAREST);
        
        var monitors = Utils.MonitorManager.GetActiveMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Handle == hMonitor.ToString());
        if (monitor == null) return;

        var desktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (!desktopId.HasValue) return;

        var engine = ZoneEngineManager.Current;
        var layout = engine.GetActiveLayout(monitor.PtInstance, desktopId.Value);
        
        string? currentLayoutId = engine.GetActiveLayoutId(monitor.PtInstance, desktopId.Value);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_overlay == null) _overlay = new OverlayWindow();
            
            var monitorInfo = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            User32.GetMonitorInfoW(hMonitor, ref monitorInfo);
            
            // Only update layout visualization if monitor or layout changed
            if (hMonitor != _lastHMonitor || currentLayoutId != _lastLayoutId)
            {
                if (layout != null)
                {
                    _overlay.ShowLayout(layout, monitor);
                }
                else
                {
                    _overlay.ShowNoLayoutMessage(monitor);
                }
                _lastHMonitor = hMonitor;
                _lastLayoutId = currentLayoutId;
            }

            if (layout != null)
            {
                var hit = engine.DetectZoneAtPoint(pt.X, pt.Y, monitor.PtInstance, desktopId.Value, monitorInfo.rcWork);
                _overlay.HighlightZone(hit?.ZoneIndex ?? -1);
            }
        });
    }

    private void HideOverlay()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Hide();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_hook != nint.Zero) User32.UnhookWinEvent(_hook);
        _disposed = true;
    }
}
