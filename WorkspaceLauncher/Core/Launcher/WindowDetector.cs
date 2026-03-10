using System.Diagnostics;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Detects which window belongs to a launched process.
/// Port of the Python window detection and fuzzy matching logic.
/// </summary>
public static class WindowDetector
{
    /// <summary>
    /// Wait for a new visible window to appear for the given process.
    /// Returns the HWND or 0 if timed out.
    /// </summary>
    public static async Task<nint> WaitForWindowAsync(Process proc, int timeoutMs = 10_000, int pollIntervalMs = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            proc.Refresh();
            if (proc.HasExited) return 0;

            var windows = WindowManager.GetWindowsByPid(proc.Id);
            var mainWin = windows.FirstOrDefault(h => IsMainWindow(h));
            if (mainWin != 0) return mainWin;

            await Task.Delay(pollIntervalMs);
        }
        return 0;
    }

    /// <summary>
    /// Find a new window that appeared after the given snapshot.
    /// </summary>
    public static async Task<nint> WaitForNewWindowAsync(HashSet<nint> before, int timeoutMs = 10_000, int pollIntervalMs = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var newWindows = WindowManager.GetNewWindows(before);
            if (newWindows.Count > 0)
            {
                // Prefer window with a larger size (likely the main window)
                var best = newWindows.MaxBy(h =>
                {
                    User32.GetWindowRect(h, out var r);
                    return r.Width * r.Height;
                });
                if (best != 0) return best;
            }
            await Task.Delay(pollIntervalMs);
        }
        return 0;
    }

    /// <summary>
    /// Fuzzy-match: find which item zone a window's current rect belongs to.
    /// Returns zone index or -1.
    /// </summary>
    public static int DetectZoneForWindow(nint hwnd, IReadOnlyList<RECT> zoneRects, int threshold = 50)
    {
        var winRect = WindowManager.GetWindowRect(hwnd);
        int bestIdx = -1;
        int bestDist = int.MaxValue;

        for (int i = 0; i < zoneRects.Count; i++)
        {
            int dist = RectDistance(winRect, zoneRects[i]);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx  = i;
            }
        }

        return bestDist <= threshold ? bestIdx : -1;
    }

    private static bool IsMainWindow(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd)) return false;
        User32.GetWindowRect(hwnd, out RECT r);
        return r.Width > 100 && r.Height > 100;
    }

    private static int RectDistance(RECT a, RECT b)
        => Math.Abs(a.Left - b.Left) + Math.Abs(a.Top - b.Top) +
           Math.Abs(a.Right - b.Right) + Math.Abs(a.Bottom - b.Bottom);
}
