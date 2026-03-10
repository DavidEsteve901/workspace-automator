using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Detects which window belongs to a launched process.
/// Port of the Python window detection and scoring logic (Phases 3 and 6).
/// </summary>
public static class WindowDetector
{
    /// <summary>
    /// PHASE 3: Wait for a visible window to appear for the given process (PID path).
    /// Returns the HWND or 0 if timed out.
    /// </summary>
    public static async Task<nint> WaitForWindowAsync(Process proc, int timeoutMs = 10_000, int pollIntervalMs = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            try { proc.Refresh(); } catch { return 0; }
            if (proc.HasExited) return 0;

            var windows = WindowManager.GetWindowsByPid(proc.Id);
            var mainWin = windows.FirstOrDefault(h => IsMainWindow(h));
            if (mainWin != 0) return mainWin;

            await Task.Delay(pollIntervalMs);
        }
        return 0;
    }

    /// <summary>
    /// PHASE 3 fallback: Find a new window that appeared after the snapshot,
    /// filtered by keywords heuristic to avoid false positives.
    /// </summary>
    public static async Task<nint> WaitForNewWindowAsync(
        HashSet<nint> before,
        string? typeHint = null,
        string? pathHint = null,
        int timeoutMs = 10_000,
        int pollIntervalMs = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var keywords = BuildKeywords(typeHint, pathHint);

        while (DateTime.UtcNow < deadline)
        {
            var newWindows = WindowManager.GetNewWindows(before);
            if (newWindows.Count > 0)
            {
                // Score and pick the best matching window
                nint best = PickBestNewWindow(newWindows, keywords);
                if (best != 0) return best;
            }
            await Task.Delay(pollIntervalMs);
        }
        return 0;
    }

    /// <summary>
    /// PHASE 6: Scoring system to match open windows to a configured AppItem.
    /// Returns best HWND or 0.
    /// Score  10 = exact process path match
    /// Score   9 = configured path appears in window title
    /// Score   8 = specific keywords (project/domain) in title or process name
    /// Score   3 = generic type match (browser, code, powershell)
    /// Score  -5 = penalize File Explorer opening a folder named like a project
    /// </summary>
    public static nint ScoreMatchBestWindow(
        Config.AppItem item,
        List<nint> candidates)
    {
        nint bestHwnd = 0;
        int bestScore  = -1;

        string itemPathLower    = (item.Path ?? "").ToLowerInvariant();
        string itemFileLower    = Path.GetFileName(itemPathLower);
        string folderNameLower  = Path.GetFileName(itemPathLower.TrimEnd('\\', '/'));

        foreach (var hwnd in candidates)
        {
            if (!User32.IsWindowVisible(hwnd)) continue;

            string title = GetTitle(hwnd).ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(title)) continue;

            string procPath  = GetProcessPath(hwnd).ToLowerInvariant();
            string procName  = Path.GetFileNameWithoutExtension(procPath);

            int score = 0;

            switch (item.Type)
            {
                case "exe":
                    if (procPath == itemPathLower)               score = 10;
                    else if (procPath.Contains(itemFileLower) && !string.IsNullOrEmpty(itemFileLower))
                                                                score = 8;
                    else if (!string.IsNullOrEmpty(itemFileLower) && title.Contains(Path.GetFileNameWithoutExtension(itemFileLower)))
                                                                score = 5;
                    break;

                case "url":
                    string host = GetHost(item.Path);
                    if (!string.IsNullOrEmpty(host) && title.Contains(host))    score = 9;
                    else if (IsBrowserProcess(procName))                         score = 3;
                    // Penalise explorer opening a folder with same name as domain
                    if (procName == "explorer" && score > 0)    score -= 5;
                    break;

                case "vscode":
                    if (procName == "code")                      score = 8;
                    if (!string.IsNullOrEmpty(folderNameLower) && title.Contains(folderNameLower))
                                                                score = Math.Max(score, 9);
                    if (title.Contains("visual studio code"))    score = Math.Max(score, 7);
                    break;

                case "ide":
                    string? ideCmd = (item.IdeCmd ?? "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(ideCmd) && procName.Contains(ideCmd))
                                                                score = 8;
                    if (!string.IsNullOrEmpty(folderNameLower) && title.Contains(folderNameLower))
                                                                score = Math.Max(score, 9);
                    break;

                case "powershell":
                    if (procName is "wt" or "windowsterminal")   score = 8;
                    else if (procName is "powershell" or "pwsh")  score = 5;
                    if (!string.IsNullOrEmpty(folderNameLower) && title.Contains(folderNameLower))
                                                                score = Math.Max(score, 6);
                    break;

                case "obsidian":
                    if (procName == "obsidian")                  score = 8;
                    if (!string.IsNullOrEmpty(folderNameLower) && title.Contains(folderNameLower))
                                                                score = Math.Max(score, 9);
                    break;
            }

            // Generic path-in-title boost
            if (score == 0 && !string.IsNullOrEmpty(itemPathLower) && title.Contains(itemPathLower))
                score = 9;

            if (score > bestScore)
            {
                bestScore = score;
                bestHwnd  = hwnd;
            }
        }

        // Minimum relevance threshold to avoid false positives
        return bestScore >= 3 ? bestHwnd : 0;
    }

    /// <summary>
    /// Fuzzy-match: find which zone a window's current rect belongs to.
    /// Returns zone index or -1.
    /// </summary>
    public static int DetectZoneForWindow(nint hwnd, IReadOnlyList<RECT> zoneRects, int threshold = 50)
    {
        var winRect = WindowManager.GetWindowRect(hwnd);
        int bestIdx  = -1;
        int bestDist = int.MaxValue;

        for (int i = 0; i < zoneRects.Count; i++)
        {
            int dist = RectDistance(winRect, zoneRects[i]);
            if (dist < bestDist) { bestDist = dist; bestIdx = i; }
        }

        return bestDist <= threshold ? bestIdx : -1;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static nint PickBestNewWindow(List<nint> newWindows, HashSet<string> keywords)
    {
        // If we have keywords, prefer the one that matches
        if (keywords.Count > 0)
        {
            foreach (var hwnd in newWindows)
            {
                if (!IsMainWindow(hwnd)) continue;
                string title = GetTitle(hwnd).ToLowerInvariant();
                string proc  = Path.GetFileNameWithoutExtension(GetProcessPath(hwnd)).ToLowerInvariant();
                if (keywords.Any(k => title.Contains(k) || proc.Contains(k)))
                    return hwnd;
            }
        }

        // Fallback: biggest visible window
        return newWindows
            .Where(IsMainWindow)
            .OrderByDescending(h => { User32.GetWindowRect(h, out var r); return r.Width * r.Height; })
            .FirstOrDefault(0);
    }

    private static HashSet<string> BuildKeywords(string? typeHint, string? pathHint)
    {
        var kw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(pathHint))
        {
            kw.Add(Path.GetFileNameWithoutExtension(pathHint).ToLowerInvariant());
            kw.Add(Path.GetFileName(pathHint.TrimEnd('\\', '/')).ToLowerInvariant());
        }
        if (!string.IsNullOrEmpty(typeHint))
        {
            switch (typeHint.ToLowerInvariant())
            {
                case "url":        kw.Add("edge"); kw.Add("chrome"); kw.Add("firefox"); kw.Add("brave"); break;
                case "vscode":     kw.Add("code"); kw.Add("visual studio"); break;
                case "powershell": kw.Add("terminal"); kw.Add("powershell"); kw.Add("pwsh"); break;
                case "obsidian":   kw.Add("obsidian"); break;
            }
        }
        kw.RemoveWhere(string.IsNullOrWhiteSpace);
        return kw;
    }

    private static bool IsMainWindow(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd)) return false;
        User32.GetWindowRect(hwnd, out RECT r);
        return r.Width > 100 && r.Height > 100;
    }

    internal static string GetTitle(nint hwnd)
    {
        int len = User32.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        User32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static string GetProcessPath(nint hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var proc = Process.GetProcessById((int)pid);
            return proc.MainModule?.FileName ?? "";
        }
        catch { return ""; }
    }

    private static string GetHost(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try { return new Uri(url).Host.ToLowerInvariant(); }
        catch { return ""; }
    }

    private static bool IsBrowserProcess(string procName)
        => procName is "msedge" or "chrome" or "firefox" or "brave" or "opera";

    private static int RectDistance(RECT a, RECT b)
        => Math.Abs(a.Left - b.Left) + Math.Abs(a.Top - b.Top) +
           Math.Abs(a.Right - b.Right) + Math.Abs(a.Bottom - b.Bottom);
}
