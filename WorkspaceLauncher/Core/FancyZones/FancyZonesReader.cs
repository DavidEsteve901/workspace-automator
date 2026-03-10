using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Reads and writes PowerToys FancyZones configuration files.
/// Port of the Python FancyZones reading logic.
/// </summary>
public static class FancyZonesReader
{
    /// <summary>
    /// Sync FancyZones custom layouts from PowerToys disk files into the app's config cache.
    /// This ensures the zone picker always has layout data available.
    /// </summary>
    public static void SyncCacheFromDisk()
    {
        try
        {
            var layouts = ReadCustomLayouts();
            var config = ConfigManager.Instance.Config;
            foreach (var (uuid, obj) in layouts)
            {
                string name = obj["name"]?.GetValue<string>() ?? "Unknown";
                string type = obj["type"]?.GetValue<string>() ?? "grid";
                var infoNode = obj["info"];
                if (infoNode == null) continue;

                config.FzLayoutsCache[uuid] = new LayoutCacheEntry
                {
                    Uuid = uuid,
                    Name = name,
                    Type = type,
                    Info = JsonSerializer.Deserialize<JsonElement>(infoNode.ToJsonString())
                };
            }
            Console.WriteLine($"[FancyZonesReader] Synced {layouts.Count} layouts from PowerToys to cache.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] SyncCacheFromDisk error: {ex.Message}");
        }
    }

    public static string FzBasePath =>
        !string.IsNullOrEmpty(ConfigManager.Instance.Config.FzCustomPath) 
            ? ConfigManager.Instance.Config.FzCustomPath 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Microsoft", "PowerToys", "FancyZones");

    public static string AppliedLayoutsPath => Path.Combine(FzBasePath, "applied-layouts.json");
    public static string CustomLayoutsPath  => Path.Combine(FzBasePath, "custom-layouts.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Read all custom layouts. Returns dict uuid → layout node.
    /// </summary>
    public static Dictionary<string, JsonObject> ReadCustomLayouts()
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(CustomLayoutsPath)) return result;

        try
        {
            string json   = File.ReadAllText(CustomLayoutsPath);
            var    root   = JsonNode.Parse(json);
            var    layouts = root?["custom-layouts"]?.AsArray();
            if (layouts == null) return result;

            foreach (var layout in layouts)
            {
                if (layout is not JsonObject obj) continue;
                string? uuid = obj["uuid"]?.GetValue<string>();
                if (uuid != null)
                    result[uuid.Trim('{', '}').ToLowerInvariant()] = obj;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] ReadCustomLayouts error: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Read applied layouts. Returns list of info (deviceId, desktopId, layoutUuid, monitorName, instance, monitorNumber, serialNumber).
    /// </summary>
    public static List<object> ReadAppliedLayouts()
    {
        var result = new List<object>();
        if (!File.Exists(AppliedLayoutsPath)) return result;

        try
        {
            string json   = File.ReadAllText(AppliedLayoutsPath);
            var    root   = JsonNode.Parse(json);
            var    applied = root?["applied-layouts"]?.AsArray();
            if (applied == null) return result;

            foreach (var entry in applied)
            {
                if (entry is not JsonObject obj) continue;
                
                var device = obj["device"];
                string? instance = device?["monitor-instance"]?.GetValue<string>();
                string? monitorName = device?["monitor"]?.GetValue<string>();
                string? deviceId = string.IsNullOrEmpty(instance) ? monitorName : instance;
                int monitorNumber = device?["monitor-number"]?.GetValue<int>() ?? 0;
                string? serialNumber = device?["serial-number"]?.GetValue<string>();
                
                if (device == null)
                    deviceId = obj["device-id"]?.GetValue<string>();

                // Check both property names used by PowerToys
                var desktopNode = obj["virtual-desktop-id"] ?? device?["virtual-desktop"];
                string? desktopId = desktopNode?.GetValue<string>()?.Trim('{', '}').ToLowerInvariant();
                
                var layout = obj["applied-layout"]?["uuid"]?.GetValue<string>();
                
                if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(layout))
                {
                    var item = new {
                        deviceId,
                        monitorName,
                        instance,
                        desktopId,
                        monitorNumber,
                        serialNumber = serialNumber ?? "",
                        layoutUuid = layout.Trim('{', '}').ToLowerInvariant()
                    };
                    Console.WriteLine($"[FancyZonesReader] Applied entry: monitor={monitorName}, instance={instance}, monNum={monitorNumber}, desktop={desktopId}, layout={item.layoutUuid}");
                    result.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] ReadAppliedLayouts error: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Inject/update a layout assignment into applied-layouts.json.
    /// </summary>
    public static bool InjectLayoutAssignment(string deviceId, string layoutUuid)
    {
        if (!File.Exists(AppliedLayoutsPath)) return false;

        try
        {
            string json = File.ReadAllText(AppliedLayoutsPath);
            var    root = JsonNode.Parse(json);
            var    arr  = root?["applied-layouts"]?.AsArray();
            if (arr == null) return false;

            bool found = false;
            foreach (var entry in arr)
            {
                if (entry is not JsonObject obj) continue;
                if (obj["device-id"]?.GetValue<string>() == deviceId)
                {
                    obj["applied-layout"]!["uuid"] = $"{{{layoutUuid.ToUpperInvariant()}}}";
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                arr.Add(JsonNode.Parse($"{{\"device-id\":\"{deviceId}\",\"applied-layout\":{{\"uuid\":\"{{{layoutUuid.ToUpperInvariant()}}}\",\"type\":\"custom\"}}}}"));
            }

            File.WriteAllText(AppliedLayoutsPath, root!.ToJsonString(JsonOpts));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] InjectLayout error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Inject/update a layout assignment for a specific monitor+desktop combination.
    /// Uses the FancyZones v2 format with device { monitor-instance, monitor, virtual-desktop }.
    /// </summary>
    public static bool InjectLayoutByDevice(string monitorInstance, string monitorName, string? virtualDesktopId, string layoutUuid)
    {
        if (!File.Exists(AppliedLayoutsPath)) return false;

        try
        {
            string json = File.ReadAllText(AppliedLayoutsPath);
            var root = JsonNode.Parse(json);
            var arr = root?["applied-layouts"]?.AsArray();
            if (arr == null) return false;

            string wrappedLayoutUuid = $"{{{layoutUuid.ToUpperInvariant()}}}";
            string wrappedDesktopId = !string.IsNullOrEmpty(virtualDesktopId) 
                ? $"{{{virtualDesktopId.Trim('{', '}').ToUpperInvariant()}}}" 
                : "{00000000-0000-0000-0000-000000000000}";

            bool found = false;
            foreach (var entry in arr)
            {
                if (entry is not JsonObject obj) continue;
                var device = obj["device"];
                if (device == null) continue;

                string? entryInstance = device["monitor-instance"]?.GetValue<string>();
                string? entryMonitor = device["monitor"]?.GetValue<string>();
                string? entryDesktop = device["virtual-desktop"]?.GetValue<string>();

                // Match by monitor-instance (most reliable) or fallback to monitor name
                bool monitorMatch = (!string.IsNullOrEmpty(entryInstance) && entryInstance == monitorInstance) ||
                                   (!string.IsNullOrEmpty(entryMonitor) && entryMonitor == monitorName);
                
                bool desktopMatch = string.IsNullOrEmpty(virtualDesktopId)
                    ? true  // If no desktop specified, match any  
                    : (entryDesktop?.Trim('{', '}').Equals(virtualDesktopId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) ?? false);

                if (monitorMatch && desktopMatch)
                {
                    var appliedLayout = obj["applied-layout"] as JsonObject;
                    if (appliedLayout != null)
                    {
                        appliedLayout["uuid"] = wrappedLayoutUuid;
                        appliedLayout["type"] = "custom";
                    }
                    else
                    {
                        obj["applied-layout"] = JsonNode.Parse($"{{\"uuid\":\"{wrappedLayoutUuid}\",\"type\":\"custom\"}}");
                    }
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Create a new entry
                var newEntry = new JsonObject
                {
                    ["device"] = new JsonObject
                    {
                        ["monitor"] = monitorName ?? "",
                        ["monitor-instance"] = monitorInstance ?? "",
                        ["monitor-number"] = 0,
                        ["serial-number"] = "",
                        ["virtual-desktop"] = wrappedDesktopId
                    },
                    ["applied-layout"] = new JsonObject
                    {
                        ["uuid"] = wrappedLayoutUuid,
                        ["type"] = "custom"
                    }
                };
                arr.Add(newEntry);
            }

            File.WriteAllText(AppliedLayoutsPath, root!.ToJsonString(JsonOpts));
            Console.WriteLine($"[FancyZonesReader] InjectLayoutByDevice: set {monitorInstance}/{monitorName} desktop={virtualDesktopId} → layout={layoutUuid}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] InjectLayoutByDevice error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Update or insert a custom layout definition in custom-layouts.json.
    /// This allows absolute portability of workspaces.
    /// </summary>
    public static bool UpsertCustomLayout(string uuid, string name, JsonElement info)
    {
        if (!File.Exists(CustomLayoutsPath)) return false;

        try
        {
            string json = File.ReadAllText(CustomLayoutsPath);
            var    root = JsonNode.Parse(json);
            var    arr  = root?["custom-layouts"]?.AsArray();
            if (arr == null) return false;

            string wrappedUuid = $"{{{uuid.ToUpperInvariant()}}}";
            bool found = false;
            foreach (var entry in arr)
            {
                if (entry is not JsonObject obj) continue;
                if (obj["uuid"]?.GetValue<string>()?.Trim('{', '}').Equals(uuid, StringComparison.OrdinalIgnoreCase) == true)
                {
                    obj["name"] = name;
                    obj["info"] = JsonNode.Parse(info.GetRawText());
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var newLayout = new JsonObject
                {
                    ["uuid"] = wrappedUuid,
                    ["name"] = name,
                    ["type"] = info.TryGetProperty("type", out var t) ? t.GetString() : "grid",
                    ["info"] = JsonNode.Parse(info.GetRawText())
                };
                arr.Add(newLayout);
            }

            File.WriteAllText(CustomLayoutsPath, root!.ToJsonString(JsonOpts));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] UpsertCustomLayout error: {ex.Message}");
            return false;
        }
    }
}
