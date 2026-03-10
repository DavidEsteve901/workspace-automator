using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.ZoneEngine;

/// <summary>
/// Cycles through windows in a zone stack.
/// Port of _cycle_zone_forward / _cycle_zone_backward + _detect_zone_for_window.
/// </summary>
public sealed class ZoneCycler
{
    public static readonly ZoneCycler Instance = new();
    private ZoneCycler() { }

    private readonly Dictionary<ZoneStack.ZoneKey, int> _positions = [];
    private DateTime _lastCycleTime = DateTime.MinValue;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    public void CycleForward(ZoneStack.ZoneKey key)
    {
        if (!CheckDebounce()) return;

        var stack = ZoneStack.Instance.GetStack(key);
        if (stack.Count < 2) return;

        _positions.TryGetValue(key, out int pos);
        pos = (pos + 1) % stack.Count;
        _positions[key] = pos;

        BringToFront(stack[pos]);
    }

    public void CycleBackward(ZoneStack.ZoneKey key)
    {
        if (!CheckDebounce()) return;

        var stack = ZoneStack.Instance.GetStack(key);
        if (stack.Count < 2) return;

        _positions.TryGetValue(key, out int pos);
        pos = (pos - 1 + stack.Count) % stack.Count;
        _positions[key] = pos;

        BringToFront(stack[pos]);
    }

    private bool CheckDebounce()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCycleTime < DebounceInterval) return false;
        _lastCycleTime = now;
        return true;
    }

    /// <summary>
    /// Detect which zone the currently active/foreground window belongs to.
    /// Returns the ZoneKey or null if no match is found.
    /// This is the C# port of Python's _detect_zone_for_window() + _get_active_zone_context().
    /// </summary>
    public ZoneStack.ZoneKey? DetectActiveWindowZoneKey()
    {
        nint hwnd = User32.GetForegroundWindow();
        if (hwnd == 0) return null;

        // Fast path: check if hwnd is already registered in a zone stack
        var registeredKey = ZoneStack.Instance.FindKeyForHwnd(hwnd);
        if (registeredKey != null) return registeredKey;

        // Slow path: fuzzy detection via window position
        return DetectZoneByPosition(hwnd);
    }

    private static ZoneStack.ZoneKey? DetectZoneByPosition(nint hwnd)
    {
        // Get window center point
        var rect = WindowManager.GetWindowRect(hwnd);
        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;

        // Get current desktop
        Guid? desktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId();
        if (!desktopId.HasValue) return null;

        // Get the monitor this window is on
        nint hMonitor = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        var monitors = WindowManager.GetMonitors();

        // Find which monitor index matches
        string monitorName = "Por defecto";
        RECT workArea = default;
        bool foundMonitor = false;

        // Get detailed monitor info for PtInstance-based key matching
        var detailedMonitors = Utils.MonitorManager.GetActiveMonitors();

        for (int i = 0; i < monitors.Count; i++)
        {
            var mwa = monitors[i].WorkArea;
            if (cx >= mwa.Left && cx <= mwa.Right && cy >= mwa.Top && cy <= mwa.Bottom)
            {
                // Use PtInstance as the monitor key (matches ZoneStack registration in orchestrator)
                monitorName = i < detailedMonitors.Count
                    ? detailedMonitors[i].PtInstance
                    : monitors[i].Name;
                workArea = mwa;
                foundMonitor = true;
                break;
            }
        }

        if (!foundMonitor && monitors.Count > 0)
        {
            workArea = monitors[0].WorkArea;
            monitorName = detailedMonitors.Count > 0
                ? detailedMonitors[0].PtInstance
                : monitors[0].Name;
        }

        // Try each layout in cache
        var config = ConfigManager.Instance.Config;
        foreach (var (uuid, cacheEntry) in config.FzLayoutsCache)
        {
            var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
            if (layoutInfo == null) continue;

            // Calculate all zone rects for this layout
            int maxZones = (layoutInfo.CellChildMap?.SelectMany(r => r).Distinct().Count())
                         ?? layoutInfo.CanvasZones?.Length
                         ?? 1;

            for (int zoneIdx = 0; zoneIdx < maxZones; zoneIdx++)
            {
                RECT? zoneRect = ZoneCalculator.CalculateZoneRect(layoutInfo, zoneIdx, workArea);
                if (zoneRect == null) continue;

                var zr = zoneRect.Value;
                // Check if window center falls within this zone (with tolerance)
                if (cx >= zr.Left - 20 && cx <= zr.Right + 20 && cy >= zr.Top - 20 && cy <= zr.Bottom + 20)
                {
                    return new ZoneStack.ZoneKey(desktopId.Value, monitorName, uuid, zoneIdx);
                }
            }
        }

        return null;
    }

    private static void BringToFront(nint hwnd)
    {
        // Use the full Phase 4 anti-block force-focus sequence
        DwmHelper.ForceFocus(hwnd);
    }
}
