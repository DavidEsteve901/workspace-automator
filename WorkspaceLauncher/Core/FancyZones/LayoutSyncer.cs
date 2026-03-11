using System.Text.Json;
using System.Text.Json.Nodes;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Syncs FancyZones layouts before launching a workspace.
/// Handles portability: injects missing layouts from the app cache into PowerToys,
/// and rescales canvas layouts to match the current monitor resolution so FancyZones
/// displays accurate zone overlays on the active screen.
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
        var config = ConfigManager.Instance.Config;

        // Track which (uuid, monitor) pairs we've already synced to avoid redundant writes
        var synced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.FancyzoneUuid)) continue;
            if (item.Fancyzone == "Ninguna") continue;

            string normalizedUuid = item.FancyzoneUuid.Trim('{', '}').ToLowerInvariant();

            // Find the target monitor for this item
            var targetMonitor = FindMonitor(item.Monitor, activeMonitors);

            string syncKey = $"{normalizedUuid}_{targetMonitor?.PtInstance ?? "primary"}";
            if (!synced.Add(syncKey)) continue; // Already handled this layout+monitor combo

            // ── PORTABILITY: Inject layout definition if missing from PowerToys ──
            if (!customLayouts.ContainsKey(normalizedUuid))
            {
                if (config.FzLayoutsCache.TryGetValue(normalizedUuid, out var cached))
                {
                    Console.WriteLine($"[LayoutSyncer] Portability: injecting layout '{cached.Name}' ({normalizedUuid}) into PowerToys.");

                    // For canvas layouts: rescale zone coordinates to current monitor resolution
                    // so FancyZones displays correct overlays even if the original was on a
                    // different resolution screen.
                    var infoToInject = cached.Info;
                    if (cached.Type?.ToLowerInvariant() == "canvas" && targetMonitor != null)
                        infoToInject = RescaleCanvasLayoutInfo(cached.Info, targetMonitor.WorkArea);

                    FancyZonesReader.UpsertCustomLayout(normalizedUuid, cached.Name, cached.Type ?? "grid", infoToInject);

                    // Refresh the local cache after injection
                    customLayouts = FancyZonesReader.ReadCustomLayouts();
                }
                else
                {
                    string warningMsg = $"Layout '{item.Fancyzone}' no encontrado en PowerToys ni en caché. " +
                                        $"Crea el layout '{item.Fancyzone?.Split('-')[0].Trim()}' manualmente en FancyZones y reasígnalo.";
                    Console.WriteLine($"[LayoutSyncer] {warningMsg}");
                    Core.SystemTray.TrayManager.Instance?.ShowBalloon(
                        "Layout no disponible",
                        warningMsg,
                        timeoutMs: 6000);
                    continue;
                }
            }

            // ── INJECTION: Assign layout to the target monitor+desktop in applied-layouts.json ──
            string? desktopId = null;
            if (item.Desktop != "Por defecto" && WorkspaceOrchestrator.TryParseDesktopIndex(item.Desktop, out int dIdx))
            {
                var desktops = VirtualDesktopManager.Instance.GetDesktops();
                if (dIdx - 1 >= 0 && dIdx - 1 < desktops.Count)
                    desktopId = desktops[dIdx - 1].ToString();
            }

            string ptInstance = !string.IsNullOrEmpty(targetMonitor?.PtInstance) ? targetMonitor!.PtInstance : targetMonitor?.Name ?? "";
            string ptMonitor  = !string.IsNullOrEmpty(targetMonitor?.PtName)     ? targetMonitor!.PtName     : targetMonitor?.Name ?? "";

            FancyZonesReader.InjectLayoutByDevice(ptInstance, ptMonitor, desktopId, item.FancyzoneUuid);
        }

        Console.WriteLine("[LayoutSyncer] FancyZones layouts synchronized.");
    }

    private static MonitorInfo? FindMonitor(string? monitorLabel, List<MonitorInfo> activeMonitors)
    {
        if (string.IsNullOrEmpty(monitorLabel) || monitorLabel == "Por defecto")
            return activeMonitors.FirstOrDefault(m => m.IsPrimary) ?? activeMonitors.FirstOrDefault();

        return activeMonitors.FirstOrDefault(m =>
                   m.Name.Equals(monitorLabel, StringComparison.OrdinalIgnoreCase) ||
                   m.PtName == monitorLabel ||
                   m.PtInstance == monitorLabel ||
                   m.Name.StartsWith(monitorLabel, StringComparison.OrdinalIgnoreCase) ||
                   monitorLabel.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase))
               ?? activeMonitors.FirstOrDefault(m => m.IsPrimary)
               ?? activeMonitors.FirstOrDefault();
    }

    /// <summary>
    /// When injecting a canvas layout from another machine, rescale zone coordinates
    /// from the original reference resolution to the current monitor's resolution.
    /// This ensures FancyZones zone overlays render correctly on the target screen.
    ///
    /// The app's own ZoneCalculator already handles scaling for window positioning,
    /// but FancyZones reads raw coordinates from custom-layouts.json for its UI overlay.
    /// </summary>
    private static JsonElement RescaleCanvasLayoutInfo(JsonElement info, RECT workArea)
    {
        try
        {
            double refW = info.TryGetProperty("ref-width",  out var rw) ? rw.GetDouble() : 0;
            double refH = info.TryGetProperty("ref-height", out var rh) ? rh.GetDouble() : 0;

            // If ref dimensions match current screen or are unset, no rescaling needed
            if (refW <= 0 || refH <= 0 ||
                (Math.Abs(refW - workArea.Width) < 2 && Math.Abs(refH - workArea.Height) < 2))
                return info;

            double scaleX = workArea.Width  / refW;
            double scaleY = workArea.Height / refH;

            // Parse, scale, and reserialize
            var node = JsonNode.Parse(info.GetRawText()) as JsonObject;
            if (node == null) return info;

            node["ref-width"]  = workArea.Width;
            node["ref-height"] = workArea.Height;

            var zones = node["zones"]?.AsArray();
            if (zones != null)
            {
                foreach (var zone in zones)
                {
                    if (zone is not JsonObject z) continue;
                    if (z["x"] is JsonValue xv) z["x"] = (int)(xv.GetValue<double>() * scaleX);
                    if (z["y"] is JsonValue yv) z["y"] = (int)(yv.GetValue<double>() * scaleY);
                    if (z["width"]  is JsonValue wv) z["width"]  = (int)(wv.GetValue<double>() * scaleX);
                    if (z["height"] is JsonValue hv) z["height"] = (int)(hv.GetValue<double>() * scaleY);
                }
            }

            Console.WriteLine($"[LayoutSyncer] Canvas layout rescaled: {refW}x{refH} → {workArea.Width}x{workArea.Height} (scale {scaleX:F2}x{scaleY:F2})");
            return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LayoutSyncer] Canvas rescale error: {ex.Message}");
            return info;
        }
    }
}
