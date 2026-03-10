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
    // Serialize cycling so rapid clicks queue up instead of running in parallel
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    // Per-key timestamps to allow rapid cycling on different zones independently
    private readonly Dictionary<ZoneStack.ZoneKey, DateTime> _lastCyclePerKey = [];
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(120);

    public void CycleForward(ZoneStack.ZoneKey key) => Task.Run(() => DoCycle(key, forward: true));
    public void CycleBackward(ZoneStack.ZoneKey key) => Task.Run(() => DoCycle(key, forward: false));

    private void DoCycle(ZoneStack.ZoneKey key, bool forward)
    {
        // Per-key debounce check BEFORE acquiring the lock — prevents pile-up
        // of queued tasks that all fire when the previous cycle finishes
        var now = DateTime.UtcNow;
        lock (_lastCyclePerKey)
        {
            if (_lastCyclePerKey.TryGetValue(key, out var last) && now - last < DebounceInterval)
                return;
            _lastCyclePerKey[key] = now;
        }

        // Serialize execution: if another cycle is running, skip (don't queue)
        if (!_cycleLock.Wait(0)) return;
        try
        {

            // Prune closed windows before cycling
            ZoneStack.Instance.PruneDeadWindows();

            var stack = ZoneStack.Instance.GetStack(key);
            if (stack.Count < 2) return;

            int pos = GetCurrentPos(key, stack);
            pos = forward
                ? (pos + 1) % stack.Count
                : (pos - 1 + stack.Count) % stack.Count;
            _positions[key] = pos;

            Console.WriteLine($"[ZoneCycler] Cycling {(forward ? "fwd" : "bwd")} key={key.Zone} stack={stack.Count} → pos={pos} hwnd={stack[pos]}");
            BringToFront(stack[pos]);
        }
        finally { _cycleLock.Release(); }
    }

    /// <summary>
    /// Resolve the "current" position in the stack by checking which window is
    /// actually foreground right now. This prevents phantom cycles where the
    /// stored index points to a window that's already on top — the first click
    /// would bring the same window again with no visible effect.
    /// Falls back to the stored position (clamped) if no stack window is foreground.
    /// </summary>
    private int GetCurrentPos(ZoneStack.ZoneKey key, IReadOnlyList<nint> stack)
    {
        nint fg = User32.GetForegroundWindow();
        for (int i = 0; i < stack.Count; i++)
            if (stack[i] == fg) return i;

        // No stack window is foreground — use stored position, clamped to valid range
        _positions.TryGetValue(key, out int stored);
        return stored % stack.Count;
    }

    /// <summary>
    /// Detect which zone the currently active/foreground window belongs to.
    /// </summary>
    public ZoneStack.ZoneKey? DetectActiveWindowZoneKey()
    {
        nint hwnd = User32.GetForegroundWindow();
        if (hwnd == 0) return null;
        return GetZoneKeyForHwnd(hwnd);
    }

    /// <summary>
    /// Get the zone key for a specific window handle.
    /// Fast path checks registered stacks; slow path detects by position.
    /// Used for hover-based cycling (window under cursor, not necessarily foreground).
    /// </summary>
    public ZoneStack.ZoneKey? GetZoneKeyForHwnd(nint hwnd)
    {
        if (hwnd == 0) return null;
        var registered = ZoneStack.Instance.FindKeyForHwnd(hwnd);
        if (registered != null) return registered;
        return DetectZoneByPosition(hwnd);
    }

    /// <summary>
    /// Detect zone by window position (fuzzy matching against all cached layout rects).
    /// </summary>
    public static ZoneStack.ZoneKey? DetectZoneByPosition(nint hwnd)
    {
        // Normalize to top-level window — WindowFromPoint can return child windows
        // (browser content pane, editor scroll area, etc.) which have wrong rects
        nint root = User32.GetAncestor(hwnd, User32.GA_ROOT);
        if (root != 0) hwnd = root;

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
        // Use the cycling-safe focus sequence (no Alt key injection — user may hold Alt)
        DwmHelper.FocusForCycling(hwnd);
    }
}
