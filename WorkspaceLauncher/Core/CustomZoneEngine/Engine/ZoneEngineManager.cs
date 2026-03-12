using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.CustomZoneEngine.Adapters;
using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Engine;

public static class ZoneEngineManager
{
    public static IZoneEngine Current =>
        (ConfigManager.Instance.Config.ZoneEngine ?? "fancyzones").ToLowerInvariant() switch
        {
            "custom" => CustomZoneEngineImpl.Instance,
            _        => FancyZonesAdapter.Instance,
        };
}
