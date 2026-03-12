namespace WorkspaceLauncher.Core.CustomZoneEngine.Models;

/// <summary>
/// Maps a (monitorPtInstance, desktopId) pair → CZELayout.Id.
/// Key format: "{ptInstance}|{desktopId:D}"
/// </summary>
public static class ActiveLayoutMap
{
    public static string MakeKey(string ptInstance, Guid desktopId)
        => $"{ptInstance}|{desktopId:D}";
}
