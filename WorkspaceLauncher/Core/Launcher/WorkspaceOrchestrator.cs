using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.NativeInterop;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Main workspace launch orchestrator.
/// Port of launch_workspace() and _launch_and_snap_intent in Python.
/// </summary>
public sealed class WorkspaceOrchestrator
{
    public static readonly WorkspaceOrchestrator Instance = new();

    public event Action<string, int>? ProgressChanged; // (message, percent)

    private WorkspaceOrchestrator() { }

    public async Task LaunchWorkspaceAsync(string category)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items) || items.Count == 0)
        {
            Report("No items in workspace.", 0);
            return;
        }

        Report($"Iniciando workspace: {category}", 0);

        // 1. Sync FancyZones layouts
        Report("Sincronizando layouts FancyZones...", 5);
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);

        // 2. Ensure virtual desktops
        Report("Verificando escritorios virtuales...", 10);
        await EnsureVirtualDesktopsAsync(items);

        // 3. Launch items
        var allLaunched = new List<(AppItem Item, nint Hwnd)>();
        int total = items.Count;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            int percent = 15 + (int)((double)(i + 1) / total * 70);
            Report($"Lanzando: {GetItemDisplayName(item)}", percent);

            if (int.TryParse(item.Delay, out int delayMs) && delayMs > 0)
                await Task.Delay(delayMs);

            nint hwnd = await LaunchAndSnapAsync(item, config);
            if (hwnd != 0) allLaunched.Add((item, hwnd));
        }

        // 4. Final integrity sweep
        Report("Barrido de integridad final...", 90);
        await Task.Delay(2000); // Wait for slow windows

        var currentWindows = WindowManager.GetVisibleWindows();
        foreach (var item in items)
        {
            // Skip items with no zone
            if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) continue;

            // Try to find the window again if it's not already in our tracked list or if we need to verify
            nint hwnd = FindWindowForItem(item, currentWindows);
            if (hwnd != 0)
            {
                RECT? zoneRect = ResolveZoneRect(item, config);
                if (zoneRect.HasValue)
                {
                    await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
                }
            }
        }

        Report("Workspace lanzado correctamente.", 100);
    }

    private nint FindWindowForItem(AppItem item, List<nint> windows)
    {
        string targetMatch = Path.GetFileName(item.Path).ToLowerInvariant();
        
        foreach (var hwnd in windows)
        {
            string title = WindowManager.GetWindowTitle(hwnd).ToLowerInvariant();
            if (string.IsNullOrEmpty(title)) continue;

            // For URLs, try to match by part of the URL in title (if browser shows it)
            if (item.Type == "url" && title.Contains(new Uri(item.Path).Host.ToLowerInvariant())) return hwnd;

            // For Obsidian
            if (item.Type == "obsidian" && title.Contains("obsidian") && title.Contains(targetMatch)) return hwnd;

            // General match by filename or cmd name
            if (title.Contains(targetMatch)) return hwnd;
            
            if (item.Type == "powershell" && (title.Contains("terminal") || title.Contains("powershell"))) return hwnd;
        }
        return 0;
    }

    private async Task EnsureVirtualDesktopsAsync(List<AppItem> items)
    {
        var needed = items
            .Select(i => i.Desktop)
            .Where(d => d != "Por defecto")
            .Distinct()
            .ToList();

        var vdm      = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();

        foreach (var desktopName in needed)
        {
            // Parse "Escritorio N"
            if (!TryParseDesktopIndex(desktopName, out int idx)) continue;
            while (desktops.Count < idx)
            {
                var newId = vdm.CreateDesktop();
                if (newId.HasValue) desktops.Add(newId.Value);
                else break;
                await Task.Delay(200);
            }
        }
    }

    private async Task<nint> LaunchAndSnapAsync(AppItem item, AppConfig config)
    {
        var vdm      = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();

        // Switch to target desktop
        if (item.Desktop != "Por defecto" && TryParseDesktopIndex(item.Desktop, out int dIdx))
        {
            if (dIdx - 1 < desktops.Count)
                vdm.SwitchToDesktop(desktops[dIdx - 1]);
        }

        var before  = new HashSet<nint>(WindowManager.GetVisibleWindows());
        var process = ProcessLauncher.Launch(item);

        nint hwnd = 0;
        if (process != null)
        {
            try
            {
                hwnd = await WindowDetector.WaitForWindowAsync(process, timeoutMs: 12_000);
            }
            catch { }
        }

        if (hwnd == 0)
            hwnd = await WindowDetector.WaitForNewWindowAsync(before, timeoutMs: 12_000);

        if (hwnd == 0)
        {
            Console.WriteLine($"[Orchestrator] Could not find window for {item.Path}");
            return 0;
        }

        // Snap to zone
        RECT? zoneRect = ResolveZoneRect(item, config);
        if (zoneRect.HasValue)
        {
            await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
        }
        
        return hwnd;
    }

    private RECT? ResolveZoneRect(AppItem item, AppConfig config)
    {
        if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) return null;

        // Find layout in cache
        string uuid = item.FancyzoneUuid.ToLowerInvariant().Trim('{', '}');
        if (!config.FzLayoutsCache.TryGetValue(uuid, out var cacheEntry)) return null;

        // Parse zone index from fancyzone name ("Entera - Zona 1" → 0)
        int zoneIdx = ParseZoneIndex(item.Fancyzone);

        // Get monitor work area
        var workArea = GetMonitorWorkArea(item.Monitor);
        if (workArea == null) return null;

        // Parse layout info
        var layoutInfo = ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return null;

        return ZoneCalculator.CalculateZoneRect(layoutInfo, zoneIdx, workArea.Value);
    }

    private static int ParseZoneIndex(string zoneName)
    {
        // "Layout Name - Zona N" → N-1
        int idx = zoneName.LastIndexOf("Zona ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && int.TryParse(zoneName[(idx + 5)..].Trim(), out int n))
            return n - 1;
        return 0;
    }

    private static RECT? GetMonitorWorkArea(string monitorLabel)
    {
        var monitors = WindowManager.GetMonitors();
        if (monitors.Count == 0) return null;

        // Frontend labels are "Pantalla N [Name]"
        // We try to extract N-1 index
        var match = System.Text.RegularExpressions.Regex.Match(monitorLabel, @"Pantalla\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
        {
            if (idx >= 1 && idx <= monitors.Count)
                return monitors[idx - 1].WorkArea;
        }

        // Fallback: match by the bracketed name [AUS2723]
        foreach (var m in monitors)
        {
            if (monitorLabel.Contains($"[{m.Name}]", StringComparison.OrdinalIgnoreCase))
                return m.WorkArea;
        }

        return monitors[0].WorkArea;
    }

    private static LayoutInfo? ParseLayoutInfo(Config.LayoutCacheEntry entry)
    {
        try
        {
            var info = entry.Info;
            return new LayoutInfo
            {
                Type    = entry.Type,
                Rows    = info.TryGetProperty("rows",    out var r) ? r.GetInt32() : 1,
                Columns = info.TryGetProperty("columns", out var c) ? c.GetInt32() : 1,
                RowsPercentage    = info.TryGetProperty("rows-percentage",    out var rp) ? rp.Deserialize<int[]>() : null,
                ColumnsPercentage = info.TryGetProperty("columns-percentage", out var cp) ? cp.Deserialize<int[]>() : null,
                CellChildMap      = info.TryGetProperty("cell-child-map",     out var cm) ? cm.Deserialize<int[][]>() : null,
                ShowSpacing       = info.TryGetProperty("show-spacing",       out var ss) && ss.GetBoolean(),
                Spacing           = info.TryGetProperty("spacing",            out var sp) ? sp.GetInt32() : 0,
            };
        }
        catch { return null; }
    }

    private static bool TryParseDesktopIndex(string name, out int index)
    {
        // "Escritorio N" → N
        index = 0;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[^1], out index);
    }

    private static string GetItemDisplayName(AppItem item) =>
        item.Type switch
        {
            "exe"        => Path.GetFileNameWithoutExtension(item.Path),
            "url"        => new Uri(item.Path).Host,
            "ide"        => $"{item.IdeCmd}: {Path.GetFileName(item.Path)}",
            "vscode"     => $"VSCode: {Path.GetFileName(item.Path)}",
            "powershell" => $"Terminal: {Path.GetFileName(item.Path)}",
            "obsidian"   => $"Obsidian: {Path.GetFileName(item.Path)}",
            _            => item.Path
        };

    private void Report(string msg, int percent)
    {
        Console.WriteLine($"[Orchestrator] {percent}% {msg}");
        ProgressChanged?.Invoke(msg, percent);
    }
}
