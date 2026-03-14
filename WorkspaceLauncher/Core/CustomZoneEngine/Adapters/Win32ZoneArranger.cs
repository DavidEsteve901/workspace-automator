using WorkspaceLauncher.Core.CustomZoneEngine.Interfaces;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.CustomZoneEngine.Adapters;

public sealed class Win32ZoneArranger : IZoneArranger
{
    public static readonly Win32ZoneArranger Instance = new();
    private Win32ZoneArranger() { }

    public bool SnapWindow(nint hwnd, RECT targetRect)
    {
        if (hwnd == nint.Zero) return false;

        // Restore minimized/maximized — SetWindowPos is silently ignored on maximized windows
        if (User32.IsIconic(hwnd) || User32.IsZoomed(hwnd))
            User32.ShowWindow(hwnd, User32.SW_RESTORE);

        // Compensate for DWM invisible shadow border
        var (shadowL, shadowT, shadowR, shadowB) = DwmHelper.GetShadowOffsets(hwnd);

        int x = targetRect.Left   - shadowL;
        int y = targetRect.Top    - shadowT;
        int w = targetRect.Width  + shadowL + shadowR;
        int h = targetRect.Height + shadowT + shadowB;

        return User32.SetWindowPos(hwnd, nint.Zero, x, y, w, h,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }
}


