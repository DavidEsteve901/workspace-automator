using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Reads and writes PowerToys FancyZones configuration files.
/// Port of the Python FancyZones reading logic.
/// </summary>

public class FzLayoutInfo
{
    public string uuid { get; set; } = "";
    public string name { get; set; } = "";
    public string type { get; set; } = "custom";
    public bool isCustom { get; set; } = true;
    public int zoneCount { get; set; } = 1;
    public object? info { get; set; }
}

public class AppliedLayoutEntry
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("monitorName")]
    public string MonitorName { get; set; } = "";

    [JsonPropertyName("instance")]
    public string Instance { get; set; } = "";

    [JsonPropertyName("desktopId")]
    public string DesktopId { get; set; } = "";

    [JsonPropertyName("monitorNumber")]
    public int MonitorNumber { get; set; }

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; set; } = "";

    [JsonPropertyName("layoutUuid")]
    public string LayoutUuid { get; set; } = "";

    [JsonPropertyName("type")]
    public string LayoutType { get; set; } = "custom";

    /// <summary>Spacing override from applied-layouts.json (-1 = not set, use layout default)</summary>
    public int Spacing { get; set; } = -1;
    public bool ShowSpacing { get; set; } = true;
}

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
            if (!File.Exists(CustomLayoutsPath)) return;
            var lastWrite = File.GetLastWriteTime(CustomLayoutsPath);
            // If already synced and file hasn't changed, skip
            if (_lastCustomLayoutsRead >= lastWrite && ConfigManager.Instance.Config.FzLayoutsCache.Count > 0)
                return;

            var layouts = ReadCustomLayouts();
            var config = ConfigManager.Instance.Config;
            
            // Note: We don't Clear() because we want to keep older layouts 
            // from PT if they were removed but our config still references them, 
            // but we update existing ones.
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
    public static string SettingsPath       => Path.Combine(FzBasePath, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    
    // Cache for custom layouts
    private static Dictionary<string, JsonObject> _customLayoutsCache = [];
    private static DateTime _lastCustomLayoutsRead = DateTime.MinValue;

    // Cache for applied layouts
    private static List<AppliedLayoutEntry> _appliedLayoutsCache = [];
    private static DateTime _lastAppliedLayoutsRead = DateTime.MinValue;

    // Cache for template info
    private static List<FzTemplateInfo> _templatesCache = [];
    private static DateTime _lastTemplatesRead = DateTime.MinValue;

    /// <summary>
    /// Forces all caches to be considered stale so the next call to any Read* method
    /// fetches fresh data from disk.  Call this whenever the FancyZones sync toggle
    /// is flipped (on→off or off→on) to prevent stale-cache false negatives.
    /// </summary>
    public static void InvalidateCaches()
    {
        _lastCustomLayoutsRead  = DateTime.MinValue;
        _lastAppliedLayoutsRead = DateTime.MinValue;
        _lastTemplatesRead      = DateTime.MinValue;
        _customLayoutsCache     = [];
        _appliedLayoutsCache    = [];
        _templatesCache         = [];
        Console.WriteLine("[FancyZonesReader] Caches invalidated.");
    }

    private static bool IsFileChanged(string path, ref DateTime lastRead)
    {
        if (!File.Exists(path)) return false;
        var lastWrite = File.GetLastWriteTime(path);
        if (lastWrite > lastRead)
        {
            lastRead = lastWrite;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Read all custom layouts. Returns dict uuid → layout node.
    /// </summary>
    public static Dictionary<string, JsonObject> ReadCustomLayouts()
    {
        if (!File.Exists(CustomLayoutsPath)) return new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        
        var lastWrite = File.GetLastWriteTime(CustomLayoutsPath);
        if (lastWrite <= _lastCustomLayoutsRead && _customLayoutsCache.Count > 0)
            return _customLayoutsCache;

        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
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
            _customLayoutsCache = result;
            _lastCustomLayoutsRead = lastWrite;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] ReadCustomLayouts error: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Returns a consolidated list of all layouts (custom + settings templates).
    /// </summary>
    public static List<FzLayoutInfo> GetAvailableLayouts()
    {
        SyncCacheFromDisk();
        var config = ConfigManager.Instance.Config;
        var layoutsCache = config?.FzLayoutsCache ?? new Dictionary<string, LayoutCacheEntry>();

        var availableLayouts = layoutsCache.Values.Select(l => {
            int zones = 0;
            try {
                if (l.Info.TryGetProperty("cell-child-map", out var map))
                {
                    var allCells = new HashSet<int>();
                    for (int rr = 0; rr < map.GetArrayLength(); rr++)
                        foreach (var cell in map[rr].EnumerateArray())
                            allCells.Add(cell.GetInt32());
                    zones = allCells.Count;
                }
                else if (l.Info.TryGetProperty("zones", out var zArray))
                    zones = zArray.GetArrayLength();
            } catch { }

            return new FzLayoutInfo {
                uuid = l.Uuid.Trim('{', '}').ToLowerInvariant(),
                name = l.Name,
                type = l.Type,
                isCustom = true,
                zoneCount = zones > 0 ? zones : 1,
                info = l.Info
            };
        }).ToList();

        // Add Default PowerToys Templates (Only if not already overridden by custom layouts)
        var ptTemplates = ReadTemplatesFromSettings();
        foreach (var template in ptTemplates)
        {
            string cleanUuid = template.Uuid.Trim('{', '}').ToLowerInvariant();
            if (!availableLayouts.Any(l => l.uuid.Equals(cleanUuid, StringComparison.OrdinalIgnoreCase)))
            {
                var info = GetDefaultTemplateInfo(template.Type);
                availableLayouts.Add(new FzLayoutInfo {
                    uuid = cleanUuid,
                    name = template.Name,
                    type = template.Type,
                    isCustom = false,
                    zoneCount = template.Type == "focus" ? 1 : 3,
                    info = info
                });
            }
        }

        return availableLayouts;
    }

    private static JsonElement? GetDefaultTemplateInfo(string type)
    {
        string json = "";
        if (type == "grid") json = "{\"type\":\"grid\",\"rows\":1,\"columns\":3,\"rows-percentage\":[10000],\"columns-percentage\":[3333,3333,3334],\"cell-child-map\":[[0,1,2]]}";
        else if (type == "rows") json = "{\"type\":\"grid\",\"rows\":3,\"columns\":1,\"rows-percentage\":[3333,3333,3334],\"columns-percentage\":[10000],\"cell-child-map\":[[0],[1],[2]]}";
        else if (type == "columns") json = "{\"type\":\"grid\",\"rows\":1,\"columns\":3,\"rows-percentage\":[10000],\"columns-percentage\":[3333,3333,3334],\"cell-child-map\":[[0,1,2]]}";
        else if (type == "priority-grid") json = "{\"type\":\"grid\",\"rows\":3,\"columns\":3,\"rows-percentage\":[3333,3333,3334],\"columns-percentage\":[2500,5000,2500],\"cell-child-map\":[[0,1,2],[0,1,2],[0,1,2]]}";
        else if (type == "focus") json = "{\"type\":\"canvas\",\"zones\":[{\"X\":2500, \"Y\":2500, \"width\":5000, \"height\":5000}]}";
        
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); } catch { return null; }
    }


    /// <summary>
    /// Reads standard PowerToys templates from settings.json.
    /// This avoids hardcoding UUIDs and improves portability.
    /// </summary>
    public static List<FzTemplateInfo> ReadTemplatesFromSettings()
    {
        var result = new List<FzTemplateInfo>();

        // 1. Add hardcoded standard templates as fallbacks
        // These are consistent across PowerToys versions
        var hardcoded = new List<FzTemplateInfo>
        {
            new FzTemplateInfo { Uuid = "grid", Name = "Cuadrícula", Type = "grid" },
            new FzTemplateInfo { Uuid = "rows", Name = "Filas", Type = "rows" },
            new FzTemplateInfo { Uuid = "columns", Name = "Columnas", Type = "columns" },
            new FzTemplateInfo { Uuid = "focus", Name = "Foco", Type = "focus" },
            new FzTemplateInfo { Uuid = "priority-grid", Name = "Cuadrícula de prioridad", Type = "priority-grid" }
        };
        result.AddRange(hardcoded);

        if (!File.Exists(SettingsPath)) return result;

        try
        {
            string json = File.ReadAllText(SettingsPath);
            var root = JsonNode.Parse(json);
            var templates = root?["properties"]?["templates"]?.AsArray();
            
            if (templates != null)
            {
                foreach (var t in templates)
                {
                    if (t is not JsonObject obj) continue;
                    string? uuid = obj["uuid"]?.GetValue<string>();
                    string? name = obj["name"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(uuid) && !string.IsNullOrEmpty(name))
                    {
                        string cleanUuid = uuid.Trim('{', '}').ToLowerInvariant();
                        string displayName = name;
                        string type = "grid"; // Default type

                        // Standardize type and name
                        if (displayName.Equals("Grid", StringComparison.OrdinalIgnoreCase)) { displayName = "Cuadrícula"; type = "grid"; }
                        else if (displayName.Equals("Priority Grid", StringComparison.OrdinalIgnoreCase)) { displayName = "Cuadrícula de prioridad"; type = "priority-grid"; }
                        else if (displayName.Equals("Rows", StringComparison.OrdinalIgnoreCase)) { displayName = "Filas"; type = "rows"; }
                        else if (displayName.Equals("Columns", StringComparison.OrdinalIgnoreCase)) { displayName = "Columnas"; type = "columns"; }
                        else if (displayName.Equals("Focus", StringComparison.OrdinalIgnoreCase)) { displayName = "Foco"; type = "focus"; }
                        else type = displayName.Replace(" ", "-").ToLowerInvariant();

                        // Deduplicate: Check if we already have this UUID OR this TYPE (for standard templates)
                        var existing = result.FirstOrDefault(r => 
                            r.Uuid.Equals(cleanUuid, StringComparison.OrdinalIgnoreCase) ||
                            (type != "custom" && r.Type.Equals(type, StringComparison.OrdinalIgnoreCase)));

                        if (existing != null)
                        {
                            // Favor the settings UUID if it's a real GUID, but keep shorthand if that's what we have
                            if (cleanUuid.Contains("-")) existing.Uuid = cleanUuid; 
                            existing.Name = displayName;
                            existing.Type = type;
                        }
                        else
                        {
                            result.Add(new FzTemplateInfo {
                                Uuid = cleanUuid,
                                Name = displayName,
                                Type = type
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FancyZonesReader] ReadTemplatesFromSettings error: {ex.Message}");
        }
        return result;
    }

    public class FzTemplateInfo 
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }

    /// <summary>
    /// Read applied layouts. Returns list of info (deviceId, desktopId, layoutUuid, monitorName, instance, monitorNumber, serialNumber).
    /// </summary>
    public static List<AppliedLayoutEntry> ReadAppliedLayouts()
    {
        if (!File.Exists(AppliedLayoutsPath)) return new List<AppliedLayoutEntry>();

        var lastWrite = File.GetLastWriteTime(AppliedLayoutsPath);
        if (lastWrite <= _lastAppliedLayoutsRead && _appliedLayoutsCache.Count > 0)
            return _appliedLayoutsCache;

        var result = new List<AppliedLayoutEntry>();
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

                var desktopNode = obj["virtual-desktop-id"] ?? device?["virtual-desktop"];
                string? desktopId = desktopNode?.GetValue<string>()?.Trim('{', '}').ToLowerInvariant();
                
                var appliedLayoutNode = obj["applied-layout"];
                var layout = appliedLayoutNode?["uuid"]?.GetValue<string>();
                string type = appliedLayoutNode?["type"]?.GetValue<string>() ?? "custom";
                int spacing = -1;
                bool showSpacing = true;
                if (appliedLayoutNode != null)
                {
                    if (appliedLayoutNode["spacing"] is { } sp && sp.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                        spacing = sp.GetValue<int>();
                    if (appliedLayoutNode["show-spacing"] is { } ss && (ss.GetValueKind() == System.Text.Json.JsonValueKind.True || ss.GetValueKind() == System.Text.Json.JsonValueKind.False))
                        showSpacing = ss.GetValue<bool>();
                }

                if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(layout))
                {
                    var item = new AppliedLayoutEntry {
                        DeviceId = deviceId,
                        MonitorName = monitorName ?? "",
                        Instance = instance ?? "",
                        DesktopId = desktopId ?? "",
                        MonitorNumber = monitorNumber,
                        SerialNumber = serialNumber ?? "",
                        LayoutUuid = layout.Trim('{', '}').ToLowerInvariant(),
                        LayoutType = type,
                        Spacing = spacing,
                        ShowSpacing = showSpacing
                    };
                    result.Add(item);
                }
            }
            _appliedLayoutsCache = result;
            _lastAppliedLayoutsRead = lastWrite;
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
            InvalidateCaches();
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
    public static bool InjectLayoutByDevice(string monitorInstance, string monitorName, string? serialNumber, string? virtualDesktopId, string layoutUuid, string type = "custom")
    {
        if (!File.Exists(AppliedLayoutsPath)) return false;

        try
        {
            string json = File.ReadAllText(AppliedLayoutsPath);
            var root = JsonNode.Parse(json);
            var arr = root?["applied-layouts"]?.AsArray();
            if (arr == null) return false;

            string wrappedLayoutUuid = $"{{{layoutUuid.Trim('{', '}').ToUpperInvariant()}}}";
            
            // Normalize search strings
            string normInstance = monitorInstance?.Trim('{', '}').ToLowerInvariant() ?? "";
            string normMonitor = monitorName?.ToLowerInvariant() ?? "";
            string normSerial = serialNumber?.ToLowerInvariant() ?? "";
            string normDesktop = virtualDesktopId?.Trim('{', '}').ToLowerInvariant() ?? "00000000-0000-0000-0000-000000000000";

            string wrappedDesktopId = normDesktop == "00000000-0000-0000-0000-000000000000" 
                ? "{00000000-0000-0000-0000-000000000000}" 
                : $"{{{normDesktop.ToUpperInvariant()}}}";

            // Detect template type for standard layouts
            var templates = ReadTemplatesFromSettings();
            var template = templates.FirstOrDefault(t => t.Uuid.Equals(layoutUuid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
            if (template != null)
            {
                type = template.Type;
                if (type != "custom")
                {
                    wrappedLayoutUuid = "{00000000-0000-0000-0000-000000000000}";
                }
            }

            bool found = false;
            foreach (var entry in arr)
            {
                if (entry is not JsonObject obj) continue;
                
                bool isMatch = false;

                // 1. Try modern PowerToys format (device object)
                var device = obj["device"];
                if (device != null)
                {
                    string? entryInstance = device["monitor-instance"]?.GetValue<string>()?.Trim('{', '}').ToLowerInvariant();
                    string? entryMonitor = device["monitor"]?.GetValue<string>()?.ToLowerInvariant();
                    string? entrySerial = device["serial-number"]?.GetValue<string>()?.ToLowerInvariant();
                    string? entryDesktop = device["virtual-desktop"]?.GetValue<string>()?.Trim('{', '}').ToLowerInvariant() 
                                           ?? "00000000-0000-0000-0000-000000000000";

                    bool serialMatch = !string.IsNullOrEmpty(normSerial) && entrySerial == normSerial;
                    bool monitorMatch = (!string.IsNullOrEmpty(normInstance) && (entryInstance == normInstance || entryInstance?.Contains(normInstance) == true)) ||
                                       (!string.IsNullOrEmpty(normMonitor) && (entryMonitor == normMonitor || entryMonitor?.Contains(normMonitor) == true));
                    
                    bool desktopMatch = entryDesktop == normDesktop || entryDesktop == "00000000-0000-0000-0000-000000000000";

                    if ((serialMatch || monitorMatch) && desktopMatch) isMatch = true;
                }
                
                // 2. Fallback to legacy PowerToys format (device-id string)
                if (!isMatch && obj["device-id"] != null)
                {
                    string entryDeviceId = obj["device-id"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
                    if (entryDeviceId.Contains(normMonitor) || entryDeviceId.Contains(normInstance))
                    {
                        isMatch = true; 
                    }
                }

                if (isMatch)
                {
                    var appliedLayout = obj["applied-layout"] as JsonObject;
                    if (appliedLayout != null)
                    {
                        appliedLayout["uuid"] = wrappedLayoutUuid;
                        appliedLayout["type"] = type;
                    }
                    else
                    {
                        obj["applied-layout"] = new JsonObject
                        {
                            ["uuid"] = wrappedLayoutUuid,
                            ["type"] = type
                        };
                    }
                    found = true;
                }
            }

            if (!found)
            {
                // Create modern entry as fallback
                var newEntry = new JsonObject
                {
                    ["device"] = new JsonObject
                    {
                        ["monitor"] = monitorName ?? "",
                        ["monitor-instance"] = (monitorInstance ?? "").StartsWith("{") ? monitorInstance : $"{{{monitorInstance?.ToUpperInvariant()}}}",
                        ["monitor-number"] = 0,
                        ["serial-number"] = serialNumber ?? "",
                        ["virtual-desktop"] = wrappedDesktopId
                    },
                    ["applied-layout"] = new JsonObject
                    {
                        ["uuid"] = wrappedLayoutUuid,
                        ["type"] = type
                    }
                };
                arr.Add(newEntry);
            }


            File.WriteAllText(AppliedLayoutsPath, root!.ToJsonString(JsonOpts));
            InvalidateCaches();
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
    public static bool UpsertCustomLayout(string uuid, string name, string type, JsonElement info)
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
                    obj["type"] = type;
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
                    ["type"] = type,
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


