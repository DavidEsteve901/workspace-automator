using System.Text.Json;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.NativeInterop;

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
        var activeMonitors = MonitorManager.GetActiveMonitors();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.FancyzoneUuid)) continue;
            if (item.Monitor == "Por defecto" || item.Fancyzone == "Ninguna") continue;

            // Ensure layout exists in PowerToys (Portability)
            string normalizedUuid = item.FancyzoneUuid.Trim('{', '}').ToLowerInvariant();
            if (!customLayouts.ContainsKey(normalizedUuid))
            {
                var config = ConfigManager.Instance.Config;
                if (config.FzLayoutsCache.TryGetValue(normalizedUuid, out var entry))
                {
                    Console.WriteLine($"[LayoutSyncer] Injecting portable layout: {entry.Name}");
                    FancyZonesReader.UpsertCustomLayout(normalizedUuid, entry.Name, entry.Type, entry.Info);
                }
            }

            // Find the active monitor to inject into
            var targetMonitor = activeMonitors.FirstOrDefault(m => 
                m.Name.Equals(item.Monitor, StringComparison.OrdinalIgnoreCase) ||
                m.PtName == item.Monitor || m.PtInstance == item.Monitor ||
                m.Name.StartsWith(item.Monitor, StringComparison.OrdinalIgnoreCase) ||
                item.Monitor.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase));
            
            if (targetMonitor == null) 
            {
                // Fallback to older logic or primary
                targetMonitor = activeMonitors.FirstOrDefault(m => m.IsPrimary) ?? activeMonitors[0];
            }

            // Resolve Desktop for FancyZones v2 injection
            string? desktopId = null;
            if (item.Desktop != "Por defecto" && WorkspaceOrchestrator.TryParseDesktopIndex(item.Desktop, out int dIdx))
            {
                var desktops = VirtualDesktopManager.Instance.GetDesktops();
                if (dIdx - 1 >= 0 && dIdx - 1 < desktops.Count)
                    desktopId = desktops[dIdx - 1].ToString();
            }

            // Provide accurate PT identifiers so that the PT engine can read the layout
            string ptInstance = !string.IsNullOrEmpty(targetMonitor.PtInstance) ? targetMonitor.PtInstance : targetMonitor.Name;
            string ptMonitor = !string.IsNullOrEmpty(targetMonitor.PtName) ? targetMonitor.PtName : targetMonitor.Name;

            FancyZonesReader.InjectLayoutByDevice(ptInstance, ptMonitor, desktopId, item.FancyzoneUuid);
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
