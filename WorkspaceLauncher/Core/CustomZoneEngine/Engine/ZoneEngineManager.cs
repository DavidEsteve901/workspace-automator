using System.Linq;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.CustomZoneEngine.Adapters;
using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Engine;

public static class ZoneEngineManager
{
    public static IZoneEngine Current =>
        (ConfigManager.Instance.Config.ZoneEngine ?? "fancyzones").ToLowerInvariant() switch
        {
            "custom" or "cze" => CustomZoneEngineImpl.Instance,
            _                 => FancyZonesAdapter.Instance,
        };

    /// <summary>
    /// True when the active zone engine is FancyZones (not CZE).
    /// Use this to gate FancyZones-specific behaviour: file sync, conflict validation,
    /// layout injection, etc.  Callers should prefer this over reading ZoneEngine directly
    /// to ensure consistent engine resolution across the codebase.
    /// </summary>
    public static bool IsFancyZonesActive =>
        !new[] { "custom", "cze" }.Contains((ConfigManager.Instance.Config.ZoneEngine ?? "fancyzones").ToLowerInvariant());

    /// <summary>
    /// True when the active zone engine is the CustomZoneEngine.
    /// </summary>
    public static bool IsCzeActive => !IsFancyZonesActive;
}


