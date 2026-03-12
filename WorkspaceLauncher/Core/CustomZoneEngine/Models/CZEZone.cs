using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Models;

/// <summary>
/// A single zone in a CZE layout — resolution-independent fractional coords (0.0–1.0).
/// </summary>
public class CZEZone
{
    public int    Id { get; set; }
    public double X  { get; set; }  // 0.0–1.0 relative to work area
    public double Y  { get; set; }
    public double W  { get; set; }
    public double H  { get; set; }

    public RECT ToPixelRect(RECT workArea) => new()
    {
        Left   = workArea.Left + (int)Math.Round(X * workArea.Width),
        Top    = workArea.Top  + (int)Math.Round(Y * workArea.Height),
        Right  = workArea.Left + (int)Math.Round((X + W) * workArea.Width),
        Bottom = workArea.Top  + (int)Math.Round((Y + H) * workArea.Height),
    };
}
