using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkspaceLauncher.Core.Config;

public class AppConfig
{
    [JsonPropertyName("apps")]
    public Dictionary<string, List<AppItem>> Apps { get; set; } = [];

    [JsonPropertyName("last_category")]
    public string LastCategory { get; set; } = string.Empty;

    [JsonPropertyName("category_order")]
    public List<string> CategoryOrder { get; set; } = [];

    [JsonPropertyName("applied_mappings")]
    public Dictionary<string, string> AppliedMappings { get; set; } = [];

    [JsonPropertyName("fz_layouts_cache")]
    public Dictionary<string, LayoutCacheEntry> FzLayoutsCache { get; set; } = [];

    [JsonPropertyName("hotkeys")]
    public HotkeyConfig Hotkeys { get; set; } = new();

    [JsonPropertyName("pip_watcher_enabled")]
    public bool PipWatcherEnabled { get; set; } = false;

    [JsonPropertyName("fz_custom_path")]
    public string? FzCustomPath { get; set; }

    [JsonPropertyName("zone_engine")]
    public string ZoneEngine { get; set; } = "fancyzones";

    [JsonPropertyName("cze_layouts")]
    public Dictionary<string, CzeLayoutEntry> CzeLayouts { get; set; } = [];

    [JsonPropertyName("cze_active_layouts")]
    public Dictionary<string, string> CzeActiveLayouts { get; set; } = [];
}

public class AppItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "exe";

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("cmd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cmd { get; set; }

    [JsonPropertyName("ide_cmd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdeCmd { get; set; }

    [JsonPropertyName("browser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Browser { get; set; }

    [JsonPropertyName("browser_display")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrowserDisplay { get; set; }

    [JsonPropertyName("monitor")]
    public string Monitor { get; set; } = "Por defecto";

    [JsonPropertyName("desktop")]
    public string Desktop { get; set; } = "Por defecto";

    [JsonPropertyName("fancyzone")]
    public string Fancyzone { get; set; } = "Ninguna";

    [JsonPropertyName("delay")]
    public string Delay { get; set; } = "0";

    [JsonPropertyName("fancyzone_uuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FancyzoneUuid { get; set; }

    [JsonPropertyName("cze_layout_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CzeLayoutId { get; set; }

    [JsonPropertyName("cze_zone_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CzeZoneIndex { get; set; }
}

public class HotkeyConfig
{
    [JsonPropertyName("cycle_forward")]
    public string CycleForward { get; set; } = "ctrl+alt+pagedown";

    [JsonPropertyName("cycle_backward")]
    public string CycleBackward { get; set; } = "ctrl+alt+pageup";


    [JsonPropertyName("desktop_cycle_fwd")]
    public string DesktopCycleFwd { get; set; } = "x1";

    [JsonPropertyName("desktop_cycle_bwd")]
    public string DesktopCycleBwd { get; set; } = "x2";

    [JsonPropertyName("util_reload_layouts")]
    public string UtilReloadLayouts { get; set; } = "ctrl+alt+l";

    [JsonPropertyName("_zone_cycle_enabled")]
    public bool ZoneCycleEnabled { get; set; } = true;

    [JsonPropertyName("_desktop_cycle_enabled")]
    public bool DesktopCycleEnabled { get; set; } = true;

    [JsonPropertyName("show_system_console")]
    public bool ShowSystemConsole { get; set; } = false;
}

public class LayoutCacheEntry
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "grid";

    [JsonPropertyName("info")]
    public JsonElement Info { get; set; }
}

public class CzeLayoutEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "New Layout";

    [JsonPropertyName("zones")]
    public List<CzeZoneEntry> Zones { get; set; } = [];
}

public class CzeZoneEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }

    [JsonPropertyName("h")]
    public double H { get; set; }
}
