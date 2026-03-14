using System;
using System.Collections.Generic;
using System.Linq;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.Launcher;

public static class WorkspaceResolver
{
    /// <summary>
    /// Checks the monitors configured in the workspace and compares them with the currently active hardware monitors.
    /// If any configured monitor is missing, it dynamically remaps it to the best available monitor.
    /// This allows portability across laptops, docking stations, and different multi-screen setups.
    /// </summary>
    public static void ResolveEnvironment(List<AppItem> items)
    {
        try
        {
            var activeMonitors = MonitorManager.GetActiveMonitors();
            if (activeMonitors.Count == 0) return;

            bool changed = false;

            foreach (var item in items)
            {
                // 1. Monitor Conflict Resolution
                if (!string.IsNullOrEmpty(item.Monitor) && item.Monitor != "Por defecto")
                {
                    if (!IsMonitorAvailable(item.Monitor, activeMonitors))
                    {
                        string newMonitor = MapMissingMonitor(item.Monitor, activeMonitors);
                        Console.WriteLine($"[Resolver] Monitor conflict detected: '{item.Monitor}' not found. Remapping to '{newMonitor}'.");
                        item.Monitor = newMonitor;
                        changed = true;
                    }
                }

                // 2. Layout UUID Repair (Portability fix for missing UUIDs)
                if (string.IsNullOrEmpty(item.FancyzoneUuid) && !string.IsNullOrEmpty(item.Fancyzone) && item.Fancyzone != "Ninguna")
                {
                    // Try to extract the base layout name (e.g., "Entera" from "Entera - Zona 1")
                    string layoutName = item.Fancyzone;
                    int dashIdx = item.Fancyzone.LastIndexOf(" - ");
                    if (dashIdx > 0) layoutName = item.Fancyzone.Substring(0, dashIdx).Trim();

                    var config = ConfigManager.Instance.Config;
                    var cachedLayout = config.FzLayoutsCache.Values.FirstOrDefault(l => l.Name.Equals(layoutName, StringComparison.OrdinalIgnoreCase));
                    if (cachedLayout != null)
                    {
                        Console.WriteLine($"[Resolver] Repaired missing UUID for layout '{layoutName}' using cache: {cachedLayout.Uuid}");
                        item.FancyzoneUuid = cachedLayout.Uuid;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                // NOTE: Intentionally NOT saving here.
                // Remappings are applied in memory for this launch session only.
                // Auto-saving would overwrite the portable config with hardware-specific
                // monitor names, breaking the workspace when switching between setups
                // (e.g. home monitor ↔ work monitor ↔ laptop standalone).
                // Users can permanently update assignments via the item editor in the UI.
                Console.WriteLine("[Resolver] Runtime environment adaptations applied (session only, not saved to disk).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Resolver] Error resolving environment: {ex.Message}");
        }
    }

    public static bool IsMonitorAvailable(string monitorLabel, List<MonitorInfo> activeMonitors)
    {
        if (string.IsNullOrEmpty(monitorLabel)) return false;

        // Try exact match with the human readable name
        if (activeMonitors.Any(m => m.Name.Equals(monitorLabel, StringComparison.OrdinalIgnoreCase))) return true;

        // Extract hardware hints from the old format like "Pantalla 1 [SDC41B6]"
        var hwMatch = System.Text.RegularExpressions.Regex.Match(monitorLabel, @"\[(.*?)\]");
        if (hwMatch.Success) 
        {
            string hwTag = hwMatch.Groups[1].Value;
            // If the active monitors have this hardware string anywhere in their name, PtName, or HardwareId
            if (activeMonitors.Any(m => m.PtName == hwTag || m.PtInstance == hwTag || m.HardwareId.Contains(hwTag) || m.Name.Contains(hwTag)))
                return true;
            
            // If it had a hardware tag and we didn't find it, consider it a conflict (missing hardware)
            return false;
        }

        // Only fall back to index matching if the string is generic (e.g. "Pantalla 1", "Escritorio 2")
        // without any specific hardware tags, or if fallback to generic matching is strictly necessary.
        if (activeMonitors.Any(m => m.Name.StartsWith(monitorLabel, StringComparison.OrdinalIgnoreCase) || monitorLabel.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase))) return true;
        if (activeMonitors.Any(m => m.PtName == monitorLabel || m.PtInstance == monitorLabel)) return true;
        
        var match = System.Text.RegularExpressions.Regex.Match(monitorLabel, @"Pantalla\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
        {
            // Only apply this fallback if the string is very short (e.g. exactly "Pantalla 2")
            if (monitorLabel.Length < 15) {
                if (idx >= 1 && idx <= activeMonitors.Count) return true;
            }
        }
        
        return false;
    }

    public static string MapMissingMonitor(string oldMonitor, List<MonitorInfo> activeMonitors)
    {
        var primary = activeMonitors.FirstOrDefault(m => m.IsPrimary) ?? activeMonitors[0];

        // If only 1 monitor, map everything there
        if (activeMonitors.Count == 1) return primary.Name;

        // Try extracting index "Pantalla 2" and map to that screen number if available
        var match = System.Text.RegularExpressions.Regex.Match(oldMonitor, @"Pantalla\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
        {
            if (idx >= 1 && idx <= activeMonitors.Count)
                return activeMonitors[idx - 1].Name;
        }

        // Try falling back to any secondary screen if it mapped to something that wasn't primary
        var secondary = activeMonitors.FirstOrDefault(m => !m.IsPrimary);
        if (secondary != null && (oldMonitor.Contains("Pantalla 2") || oldMonitor.Contains("Pantalla 3")))
        {
            return secondary.Name;
        }

        return primary.Name;
    }
}


