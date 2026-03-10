using System.Runtime.InteropServices;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Compensates for DWM shadow/frame offsets when positioning windows.
/// Port of the Python shadow compensation logic.
/// </summary>
public static class DwmHelper
{
    /// <summary>
    /// Calculate the shadow offsets for a window.
    /// Returns (left, top, right, bottom) expansion needed.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) GetShadowOffsets(nint hwnd)
    {
        User32.GetWindowRect(hwnd, out RECT winRect);
        int hr = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frameRect, (uint)Marshal.SizeOf<RECT>());
        if (hr != 0)
            return (0, 0, 0, 0);

        return (
            frameRect.Left   - winRect.Left,
            frameRect.Top    - winRect.Top,
            winRect.Right    - frameRect.Right,
            winRect.Bottom   - frameRect.Bottom
        );
    }

    /// <summary>
    /// Expand a target zone rect to account for window shadow, so that
    /// the visible content area matches the intended zone.
    /// </summary>
    public static RECT CompensateShadow(nint hwnd, RECT target)
    {
        var (l, t, r, b) = GetShadowOffsets(hwnd);
        return new RECT
        {
            Left   = target.Left   - l,
            Top    = target.Top    - t,
            Right  = target.Right  + r,
            Bottom = target.Bottom + b,
        };
    }

    /// <summary>
    /// Apply a zone rect to a window, compensating for shadows.
    /// </summary>
    public static async Task<bool> ApplyZoneRect(nint hwnd, RECT zoneRect, int retries = 5)
    {
        RECT adjusted = CompensateShadow(hwnd, zoneRect);
        return await WindowManager.SnapToRectAsync(hwnd, adjusted, retries);
    }
}
