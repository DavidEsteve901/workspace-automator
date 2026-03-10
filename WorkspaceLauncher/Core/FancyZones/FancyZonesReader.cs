using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkspaceLauncher.Core.FancyZones;

/// <summary>
/// Reads and writes PowerToys FancyZones configuration files.
/// Port of the Python FancyZones reading logic.
/// </summary>
public static class FancyZonesReader
{
    private static string FzBasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
    /// Read applied layouts. Returns dict "{uuid}_{monitor}" → layout uuid.
    /// </summary>
    public static Dictionary<string, string> ReadAppliedLayouts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                var deviceId = obj["device-id"]?.GetValue<string>();
                var layout   = obj["applied-layout"]?["uuid"]?.GetValue<string>();
                if (deviceId != null && layout != null)
                    result[deviceId] = layout.Trim('{', '}').ToLowerInvariant();
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
}
