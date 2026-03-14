using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;
using WorkspaceLauncher.Core.CustomZoneEngine.Models;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Engine;

public sealed class CustomZoneEngineImpl : IZoneEngine
{
    public static readonly CustomZoneEngineImpl Instance = new();
    private CustomZoneEngineImpl() { }

    public string EngineId => "custom";

    private AppConfig Config => ConfigManager.Instance.Config;

    public WorkspaceLauncher.Core.Config.CzeLayoutEntry? GetActiveLayout(string monitorPtInstance, Guid desktopId)
    {
        string? layoutId = GetActiveLayoutId(monitorPtInstance, desktopId);
        if (layoutId == null) return null;
        
        if (Config.CzeLayouts.TryGetValue(layoutId, out var layout)) return layout;
        
        // Fallback to virtual templates
        var template = CZETemplateHelper.GetVirtualTemplate(layoutId);
        return template != null ? CZETemplateHelper.ToConfigEntry(template) : null;
    }

    public string? GetActiveLayoutId(string monitorPtInstance, Guid desktopId)
    {
        string key = ActiveLayoutMap.MakeKey(monitorPtInstance, desktopId);
        return Config.CzeActiveLayouts.TryGetValue(key, out var id) ? id : null;
    }

    public RECT? CalculateZoneRect(string layoutId, int zoneIndex, RECT workArea)
    {
        CzeLayoutEntry? layout;
        if (!Config.CzeLayouts.TryGetValue(layoutId, out layout))
        {
            var template = CZETemplateHelper.GetVirtualTemplate(layoutId);
            if (template == null) return null;
            layout = CZETemplateHelper.ToConfigEntry(template);
        }

        if (zoneIndex < 0 || zoneIndex >= layout.Zones.Count) return null;
        var ze = layout.Zones[zoneIndex];
        var zone = new CZEZone { Id = ze.Id, X = ze.X, Y = ze.Y, W = ze.W, H = ze.H };
        return zone.ToPixelRect(workArea);
    }

    public (string LayoutId, int ZoneIndex)? DetectZoneAtPoint(int cx, int cy, string monitorPtInstance, Guid desktopId, RECT workArea)
    {
        string? layoutId = GetActiveLayoutId(monitorPtInstance, desktopId);
        if (layoutId == null) return null;

        CzeLayoutEntry? layout;
        if (!Config.CzeLayouts.TryGetValue(layoutId, out layout))
        {
            var template = CZETemplateHelper.GetVirtualTemplate(layoutId);
            if (template == null) return null;
            layout = CZETemplateHelper.ToConfigEntry(template);
        }

        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var ze = layout.Zones[i];
            var zone = new CZEZone { Id = ze.Id, X = ze.X, Y = ze.Y, W = ze.W, H = ze.H };
            RECT r = zone.ToPixelRect(workArea);
            if (cx >= r.Left - 20 && cx <= r.Right + 20 && cy >= r.Top - 20 && cy <= r.Bottom + 20)
                return (layoutId, i);
        }
        return null;
    }

    public int GetZoneCount(string layoutId, RECT workArea)
    {
        if (Config.CzeLayouts.TryGetValue(layoutId, out var layout)) return layout.Zones.Count;
        var template = CZETemplateHelper.GetVirtualTemplate(layoutId);
        return template?.Zones.Count ?? 0;
    }

    public IReadOnlyList<RECT> GetAllZoneRects(string layoutId, RECT workArea)
    {
        CzeLayoutEntry? layout;
        if (!Config.CzeLayouts.TryGetValue(layoutId, out layout))
        {
            var template = CZETemplateHelper.GetVirtualTemplate(layoutId);
            if (template == null) return [];
            layout = CZETemplateHelper.ToConfigEntry(template);
        }

        return layout.Zones
            .Select(ze => new CZEZone { Id = ze.Id, X = ze.X, Y = ze.Y, W = ze.W, H = ze.H }.ToPixelRect(workArea))
            .ToList();
    }
}

