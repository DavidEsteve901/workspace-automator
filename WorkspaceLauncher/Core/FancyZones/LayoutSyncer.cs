using System.Text.Json;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Syncs FancyZones layouts before launching a workspace.
/// Port of _sync_fz_layouts_for_workspace and _inject_layout_to_powertoys.
/// </summary>
public static class LayoutSyncer
{
    /// <summary>
    /// For each item in the workspace, inject the correct layout assignment
    /// into PowerToys FancyZones applied-layouts.json.
    /// </summary>
    public static void SyncForWorkspace(IEnumerable<AppItem> items, Dictionary<string, string> appliedMappings)
    {
        var customLayouts = FancyZonesReader.ReadCustomLayouts();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.FancyzoneUuid)) continue;
            if (item.Monitor == "Por defecto" || item.Fancyzone == "Ninguna") continue;

            // Build device-id key (same format as applied-layouts.json)
            string monitorId = NormalizeMonitorId(item.Monitor);
            if (string.IsNullOrEmpty(monitorId)) continue;

            FancyZonesReader.InjectLayoutAssignment(monitorId, item.FancyzoneUuid);
        }

        Console.WriteLine("[LayoutSyncer] FancyZones layouts synchronized.");
    }

    private static string NormalizeMonitorId(string monitorDisplay)
    {
        // Monitor display names like "Pantalla 1 [SDC41B6]" →
        // PowerToys device IDs are hardware-specific; we return the display name
        // as a fallback lookup key (actual matching done by applied_mappings in config)
        return monitorDisplay;
    }
}
