using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;

/// <summary>
/// Interface for arranging/snapping windows to zones.
/// </summary>
public interface IZoneArranger
{
    /// <summary>
    /// Snaps a window to a specific zone rect.
    /// </summary>
    bool SnapWindow(nint hwnd, RECT rect);
}
