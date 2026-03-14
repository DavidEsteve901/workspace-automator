using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Adapters;

public sealed class FancyZonesAdapter : IZoneEngine
{
    public static readonly FancyZonesAdapter Instance = new();
    private FancyZonesAdapter() { }

    public string EngineId => "fancyzones";

    public string? GetActiveLayoutId(string monitorPtInstance, Guid desktopId)
    {
        var entry = GetActiveAppliedLayout(monitorPtInstance, desktopId);
        if (entry == null) return null;
        return entry.LayoutUuid.Trim('{', '}').ToLowerInvariant();
    }

    public WorkspaceLauncher.Core.Config.CzeLayoutEntry? GetActiveLayout(string monitorPtInstance, Guid desktopId)
    {
        var entry = GetActiveAppliedLayout(monitorPtInstance, desktopId);
        if (entry == null) return null;

        string layoutId = entry.LayoutUuid.Trim('{', '}').ToLowerInvariant();
        var config = ConfigManager.Instance.Config;

        if (!config.FzLayoutsCache.TryGetValue(layoutId, out var cacheEntry)) return null;
        var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return null;

        // Convert FZ layout to CZE format for consistency (0–10000 int units)
        var dummyWorkArea = new RECT { Right = 10000, Bottom = 10000 };
        var rects = GetAllZoneRects(layoutId, dummyWorkArea);

        return new WorkspaceLauncher.Core.Config.CzeLayoutEntry
        {
            Id = layoutId,
            Name = cacheEntry.Name,
            Zones = rects.Select((r, i) => new WorkspaceLauncher.Core.Config.CzeZoneEntry
            {
                Id = i,
                X = r.Left,
                Y = r.Top,
                W = r.Width,
                H = r.Height
            }).ToList()
        };
    }

    private AppliedLayoutEntry? GetActiveAppliedLayout(string monitorPtInstance, Guid desktopId)
    {
        string desktopIdStr = desktopId.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();

        var entry = appliedLayouts.FirstOrDefault(e =>
            (e.Instance == monitorPtInstance || e.MonitorName == monitorPtInstance
                || e.MonitorName.StartsWith(monitorPtInstance) || monitorPtInstance.StartsWith(e.MonitorName))
            && (string.IsNullOrEmpty(e.DesktopId) || e.DesktopId.Equals(desktopIdStr, StringComparison.OrdinalIgnoreCase)));

        entry ??= appliedLayouts.FirstOrDefault(e =>
            e.Instance == monitorPtInstance || e.MonitorName == monitorPtInstance
                || e.MonitorName.StartsWith(monitorPtInstance) || monitorPtInstance.StartsWith(e.MonitorName));

        return entry;
    }

    public RECT? CalculateZoneRect(string layoutId, int zoneIndex, RECT workArea)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.FzLayoutsCache.TryGetValue(layoutId, out var cacheEntry)) return null;
        var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return null;
        return ZoneCalculator.CalculateZoneRect(layoutInfo, zoneIndex, workArea);
    }

    public (string LayoutId, int ZoneIndex)? DetectZoneAtPoint(int cx, int cy, string monitorPtInstance, Guid desktopId, RECT workArea)
    {
        string? layoutId = GetActiveLayoutId(monitorPtInstance, desktopId);
        if (layoutId == null) return null;

        var config = ConfigManager.Instance.Config;
        if (!config.FzLayoutsCache.TryGetValue(layoutId, out var cacheEntry)) return null;
        var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return null;

        // Apply spacing from applied-layouts
        string desktopIdStr = desktopId.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
        var entry = appliedLayouts.FirstOrDefault(e =>
            (e.Instance == monitorPtInstance || e.MonitorName == monitorPtInstance) &&
            (string.IsNullOrEmpty(e.DesktopId) || e.DesktopId.Equals(desktopIdStr, StringComparison.OrdinalIgnoreCase)));
        if (entry != null && entry.Spacing >= 0)
        {
            layoutInfo.Spacing = entry.Spacing;
            layoutInfo.ShowSpacing = entry.ShowSpacing;
        }

        int maxZones = (layoutInfo.CellChildMap?.SelectMany(r => r).Distinct().Count())
                     ?? layoutInfo.CanvasZones?.Length
                     ?? 1;

        for (int i = 0; i < maxZones; i++)
        {
            RECT? zr = ZoneCalculator.CalculateZoneRect(layoutInfo, i, workArea);
            if (zr == null) continue;
            var z = zr.Value;
            if (cx >= z.Left - 20 && cx <= z.Right + 20 && cy >= z.Top - 20 && cy <= z.Bottom + 20)
                return (layoutId, i);
        }
        return null;
    }

    public int GetZoneCount(string layoutId, RECT workArea)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.FzLayoutsCache.TryGetValue(layoutId, out var cacheEntry)) return 0;
        var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return 0;
        return (layoutInfo.CellChildMap?.SelectMany(r => r).Distinct().Count())
             ?? layoutInfo.CanvasZones?.Length
             ?? 0;
    }

    public IReadOnlyList<RECT> GetAllZoneRects(string layoutId, RECT workArea)
    {
        int count = GetZoneCount(layoutId, workArea);
        var config = ConfigManager.Instance.Config;
        if (!config.FzLayoutsCache.TryGetValue(layoutId, out var cacheEntry)) return [];
        var layoutInfo = WorkspaceOrchestrator.ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return [];

        var rects = new List<RECT>(count);
        for (int i = 0; i < count; i++)
        {
            RECT? r = ZoneCalculator.CalculateZoneRect(layoutInfo, i, workArea);
            if (r.HasValue) rects.Add(r.Value);
        }
        return rects;
    }
}


