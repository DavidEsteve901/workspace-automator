using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;

public interface IZoneEngine
{
    string EngineId { get; }

    /// <summary>Get the layout ID active for (monitorPtInstance, desktopId), or null.</summary>
    string? GetActiveLayoutId(string monitorPtInstance, Guid desktopId);

    /// <summary>Get the full layout entry active for (monitorPtInstance, desktopId), or null.</summary>
    WorkspaceLauncher.Core.Config.CzeLayoutEntry? GetActiveLayout(string monitorPtInstance, Guid desktopId);

    /// <summary>Calculate pixel RECT for a zone within a layout, given the monitor work area.</summary>
    RECT? CalculateZoneRect(string layoutId, int zoneIndex, RECT workArea);

    /// <summary>Detect which (layoutId, zoneIndex) a center point falls into.</summary>
    (string LayoutId, int ZoneIndex)? DetectZoneAtPoint(int cx, int cy, string monitorPtInstance, Guid desktopId, RECT workArea);

    /// <summary>Number of zones in the layout (for cycling).</summary>
    int GetZoneCount(string layoutId, RECT workArea);

    /// <summary>All zone rects for a layout (for overlay rendering).</summary>
    IReadOnlyList<RECT> GetAllZoneRects(string layoutId, RECT workArea);
}


