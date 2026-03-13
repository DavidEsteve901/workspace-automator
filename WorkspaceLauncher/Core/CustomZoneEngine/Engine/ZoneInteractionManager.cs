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
                    Task.Run(() => TrySnapWindow(_draggingHwnd));
                }
            }
            _isDragging = false;
            _draggingHwnd = nint.Zero;
        }
    }

    private void TrySnapWindow(nint hwnd)
    {
        User32.GetCursorPos(out POINT pt);
        nint hMonitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        
        var monitors = Utils.MonitorManager.GetActiveMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Handle == hMonitor.ToString());
        if (monitor == null) return;

        var desktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (!desktopId.HasValue) return;

        var engine = ZoneEngineManager.Current;
        
        var monitorInfo = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        User32.GetMonitorInfoW(hMonitor, ref monitorInfo);

        var hit = engine.DetectZoneAtPoint(pt.X, pt.Y, monitor.PtInstance, desktopId.Value, monitorInfo.rcWork);
        if (hit.HasValue)
        {
            var rect = engine.CalculateZoneRect(hit.Value.LayoutId, hit.Value.ZoneIndex, monitorInfo.rcWork);
            if (rect.HasValue)
            {
                Win32ZoneArranger.Instance.SnapWindow(hwnd, rect.Value);
                
                // Register in ZoneStack for cycling compatibility
                var key = new ZoneStack.ZoneKey(desktopId.Value, monitor.PtInstance, hit.Value.LayoutId, hit.Value.ZoneIndex);
                ZoneStack.Instance.Register(key, hwnd);
            }
        }
    }

    private OverlayWindow? _overlay;

    private async Task MonitorDrag()
    {
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
            }

            await Task.Delay(30);
        }
        HideOverlay();
    }

    private void UpdateOverlay()
    {
        User32.GetCursorPos(out POINT pt);
        nint hMonitor = User32.MonitorFromWindow(_draggingHwnd, User32.MONITOR_DEFAULTTONEAREST);
        
        var monitors = Utils.MonitorManager.GetActiveMonitors();
        var monIdx = monitors.FindIndex(m => m.Handle == hMonitor.ToString());
        if (monIdx == -1) return;

        var monitor = monitors[monIdx];
        var desktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (!desktopId.HasValue) return;

        var engine = ZoneEngineManager.Current;
        var layout = engine.GetActiveLayout(monitor.PtInstance, desktopId.Value);
        if (layout == null) 
        {
            HideOverlay();
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_overlay == null) _overlay = new OverlayWindow();
            
            var monitorInfo = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            User32.GetMonitorInfoW(hMonitor, ref monitorInfo);
            
            _overlay.ShowLayout(layout, monitor);

            var hit = engine.DetectZoneAtPoint(pt.X, pt.Y, monitor.PtInstance, desktopId.Value, monitorInfo.rcWork);
            if (hit.HasValue)
            {
                _overlay.HighlightZone(hit.Value.ZoneIndex);
            }
            else
            {
                _overlay.HighlightZone(-1);
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
