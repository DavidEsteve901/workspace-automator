using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Models;

/// <summary>
/// A single zone in a CZE layout — resolution-independent units (0–10000).
/// 10000 units = 100% of the work area dimension.
/// </summary>
public class CZEZone
{
    public int Id { get; set; }
    public int X  { get; set; }  // 0–10000 relative to work area
    public int Y  { get; set; }
    public int W  { get; set; }
    public int H  { get; set; }

    public RECT ToPixelRect(RECT workArea) => new()
    {
        Left   = workArea.Left + (int)Math.Round((double)X       * workArea.Width  / 10000),
        Top    = workArea.Top  + (int)Math.Round((double)Y       * workArea.Height / 10000),
        Right  = workArea.Left + (int)Math.Round((double)(X + W) * workArea.Width  / 10000),
        Bottom = workArea.Top  + (int)Math.Round((double)(Y + H) * workArea.Height / 10000),
    };
}
