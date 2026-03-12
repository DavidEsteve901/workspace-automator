namespace WorkspaceLauncher.Core.CustomZoneEngine.Models;

public class CZELayout
{
    public string         Id    { get; set; } = Guid.NewGuid().ToString("D");
    public string         Name  { get; set; } = "New Layout";
    public List<CZEZone>  Zones { get; set; } = [];
}
