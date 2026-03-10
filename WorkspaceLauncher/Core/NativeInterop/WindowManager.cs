using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkspaceLauncher.Core.NativeInterop;

/// <summary>
/// Utility methods for enumerating and positioning windows.
/// </summary>
public static class WindowManager
{
    /// <summary>
    /// Move and resize a window to the given rectangle, with a retry loop.
    /// </summary>
    public static async Task<bool> SnapToRectAsync(nint hwnd, RECT target, int maxRetries = 5, int delayMs = 300)
    {
        const uint flags = User32.SWP_NOZORDER | User32.SWP_NOACTIVATE;
        for (int i = 0; i < maxRetries; i++)
        {
            User32.ShowWindow(hwnd, User32.SW_RESTORE);
            User32.SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height, flags);
            await Task.Delay(delayMs);

            User32.GetWindowRect(hwnd, out RECT actual);
            if (RectsClose(actual, target, threshold: 8)) return true;

            // Retry
        }
        return false;
    }

    private static bool RectsClose(RECT a, RECT b, int threshold)
        => Math.Abs(a.Left - b.Left) <= threshold &&
           Math.Abs(a.Top  - b.Top)  <= threshold &&
           Math.Abs(a.Right  - b.Right)  <= threshold &&
           Math.Abs(a.Bottom - b.Bottom) <= threshold;

    /// <summary>
    /// Enumerate all visible top-level windows.
    /// </summary>
    public static List<nint> GetVisibleWindows()
    {
        var result = new List<nint>();
        User32.EnumWindows((hwnd, _) =>
        {
            if (User32.IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        }, 0);
        return result;
    }

    /// <summary>
    /// Enumerate visible windows created by a given process ID.
    /// </summary>
    public static List<nint> GetWindowsByPid(int pid)
    {
        var result = new List<nint>();
        User32.EnumWindows((hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd)) return true;
            User32.GetWindowThreadProcessId(hwnd, out uint wPid);
            if ((int)wPid == pid) result.Add(hwnd);
            return true;
        }, 0);
        return result;
    }

    /// <summary>
    /// Get all visible windows that appeared after the given snapshot.
    /// </summary>
    public static List<nint> GetNewWindows(HashSet<nint> before)
    {
        var after  = new HashSet<nint>(GetVisibleWindows());
        after.ExceptWith(before);
        return [.. after];
    }

    /// <summary>
    /// Get current RECT for a window (using DWM for accurate frame bounds).
    /// </summary>
    public static RECT GetWindowRect(nint hwnd)
    {
        int hr = Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, (uint)Marshal.SizeOf<RECT>());
        if (hr == 0) return r;
        User32.GetWindowRect(hwnd, out RECT r2);
        return r2;
    }

    /// <summary>
    /// Collect all monitors with their work areas.
    /// Returns list of (deviceName, workArea).
    /// </summary>
    public static List<(string Name, RECT WorkArea)> GetMonitors()
    {
        var monitors = new List<(string, RECT)>();
        User32.EnumDisplayMonitors(0, 0, (hMon, _, ref _, _) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (User32.GetMonitorInfoW(hMon, ref mi))
                monitors.Add(($"Monitor_{monitors.Count + 1}", mi.rcWork));
            return true;
        }, 0);
        return monitors;
    }
}
