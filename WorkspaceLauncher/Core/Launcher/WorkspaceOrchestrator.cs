using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.ZoneEngine;

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

        // Clear zone stacks for fresh launch
        ZoneStack.Instance.Clear();

        // 1. Sync FancyZones layouts
        Report("Sincronizando layouts FancyZones...", 5);
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);

        // 2. Ensure virtual desktops
        Report("Verificando escritorios virtuales...", 10);
        await EnsureVirtualDesktopsAsync(items);

        // 3. Launch items
        int total = items.Count;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            int percent = 15 + (int)((double)(i + 1) / total * 70);
            Report($"Lanzando: {GetItemDisplayName(item)}", percent);

            if (int.TryParse(item.Delay, out int delayMs) && delayMs > 0)
                await Task.Delay(delayMs);

            nint hwnd = await LaunchAndSnapAsync(item, config);
            if (hwnd != 0)
                RegisterInZoneStack(item, hwnd, config);
        }

        // 4. Final integrity sweep
        Report("Barrido de integridad final...", 90);
        await Task.Delay(2000);

        var currentWindows = WindowManager.GetVisibleWindows();
        foreach (var item in items)
        {
            if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) continue;

            nint hwnd = FindWindowForItem(item, currentWindows);
            if (hwnd != 0)
            {
                RECT? zoneRect = ResolveZoneRect(item, config);
                if (zoneRect.HasValue)
                {
                    await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
                    RegisterInZoneStack(item, hwnd, config);
                }
            }
        }

        Report("Workspace lanzado correctamente.", 100);
    }

    /// <summary>
    /// Restore workspace: find already-open windows and snap them to configured zones.
    /// </summary>
    public async Task RestoreWorkspaceAsync(string category)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items) || items.Count == 0)
        {
            Report("No items in workspace.", 0);
            return;
        }

        Report($"Restaurando workspace: {category}", 0);

        // Sync FancyZones layouts
        FancyZonesReader.SyncCacheFromDisk();
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);

        // Ensure virtual desktops exist
        await EnsureVirtualDesktopsAsync(items);

        var windows = WindowManager.GetVisibleWindows();
        int total = items.Count;
        int matched = 0;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            int percent = 10 + (int)((double)(i + 1) / total * 80);
            Report($"Buscando: {GetItemDisplayName(item)}", percent);

            nint hwnd = FindWindowForItem(item, windows);
            if (hwnd == 0) continue;

            // Switch to target desktop if needed
            var vdm = VirtualDesktopManager.Instance;
            var desktops = vdm.GetDesktops();
            if (item.Desktop != "Por defecto" && TryParseDesktopIndex(item.Desktop, out int dIdx))
            {
                if (dIdx - 1 < desktops.Count)
                    vdm.SwitchToDesktop(desktops[dIdx - 1]);
            }

            RECT? zoneRect = ResolveZoneRect(item, config);
            if (zoneRect.HasValue)
            {
                await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
                RegisterInZoneStack(item, hwnd, config);
                matched++;
            }
        }

        Report($"Workspace restaurado: {matched}/{total} ventanas reposicionadas.", 100);
    }

    /// <summary>
    /// Clean workspace: close windows matching the configured items.
    /// </summary>
    public async Task CleanWorkspaceAsync(string category)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items) || items.Count == 0)
        {
            Report("No items in workspace.", 0);
            return;
        }

        Report($"Limpiando workspace: {category}", 0);

        var windows = WindowManager.GetVisibleWindows();
        int total = items.Count;
        int closed = 0;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            int percent = 10 + (int)((double)(i + 1) / total * 80);
            Report($"Cerrando: {GetItemDisplayName(item)}", percent);

            nint hwnd = FindWindowForItem(item, windows);
            if (hwnd == 0) continue;

            try
            {
                User32.SendMessage(hwnd, User32.WM_CLOSE, 0, 0);
                ZoneStack.Instance.Unregister(hwnd);
                closed++;
                await Task.Delay(200); // Small delay between closes
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestrator] Error closing window: {ex.Message}");
            }
        }

        ZoneStack.Instance.Clear();
        Report($"Workspace limpiado: {closed} ventanas cerradas.", 100);
    }

    // ── Zone Stack Registration ──────────────────────────────────────────────

    private void RegisterInZoneStack(AppItem item, nint hwnd, AppConfig config)
    {
        if (string.IsNullOrEmpty(item.FancyzoneUuid) || item.Fancyzone == "Ninguna") return;

        string layoutUuid = item.FancyzoneUuid.Trim('{', '}').ToLowerInvariant();
        int zoneIdx = ParseZoneIndex(item.Fancyzone);
        Guid desktopGuid = ResolveDesktopGuid(item) ?? Guid.Empty;
        string monitorName = item.Monitor ?? "Por defecto";

        if (desktopGuid == Guid.Empty)
        {
            desktopGuid = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;
        }

        var zoneKey = new ZoneStack.ZoneKey(desktopGuid, monitorName, layoutUuid, zoneIdx);
        ZoneStack.Instance.Register(zoneKey, hwnd);
        Console.WriteLine($"[Orchestrator] Registered hwnd {hwnd} in zone stack: desktop={desktopGuid:N}, layout={layoutUuid}, zone={zoneIdx}");
    }

    private static Guid? ResolveDesktopGuid(AppItem item)
    {
        if (item.Desktop == "Por defecto")
            return VirtualDesktopManager.Instance.GetCurrentDesktopId();

        if (!TryParseDesktopIndex(item.Desktop, out int dIdx)) return null;

        var desktops = VirtualDesktopManager.Instance.GetDesktops();
        if (dIdx - 1 >= 0 && dIdx - 1 < desktops.Count)
            return desktops[dIdx - 1];

        return null;
    }

    // ── Window matching ──────────────────────────────────────────────────────

    internal nint FindWindowForItem(AppItem item, List<nint> windows)
    {
        string targetMatch = "";
        try { targetMatch = Path.GetFileName(item.Path).ToLowerInvariant(); } catch { }

        foreach (var hwnd in windows)
        {
            string title = WindowManager.GetWindowTitle(hwnd).ToLowerInvariant();
            if (string.IsNullOrEmpty(title)) continue;

            // For URLs, try to match by host in title
            if (item.Type == "url")
            {
                try
                {
                    if (title.Contains(new Uri(item.Path).Host.ToLowerInvariant())) return hwnd;
                }
                catch { }
            }

            // For Obsidian
            if (item.Type == "obsidian" && title.Contains("obsidian") && !string.IsNullOrEmpty(targetMatch) && title.Contains(targetMatch)) return hwnd;

            // General match by filename
            if (!string.IsNullOrEmpty(targetMatch) && title.Contains(targetMatch)) return hwnd;

            // PowerShell/Terminal match
            if (item.Type == "powershell" && (title.Contains("terminal") || title.Contains("powershell"))) return hwnd;

            // VS Code match
            if (item.Type == "vscode" && title.Contains("visual studio code")) return hwnd;
        }
        return 0;
    }

    // ── Virtual desktop management ───────────────────────────────────────────

    private async Task EnsureVirtualDesktopsAsync(List<AppItem> items)
    {
        var needed = items
            .Select(i => i.Desktop)
            .Where(d => d != "Por defecto")
            .Distinct()
            .ToList();

        var vdm = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();

        foreach (var desktopName in needed)
        {
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
        var vdm = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();

        // Switch to target desktop
        if (item.Desktop != "Por defecto" && TryParseDesktopIndex(item.Desktop, out int dIdx))
        {
            if (dIdx - 1 < desktops.Count)
                vdm.SwitchToDesktop(desktops[dIdx - 1]);
        }

        var before = new HashSet<nint>(WindowManager.GetVisibleWindows());
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

    // ── Zone rect resolution ─────────────────────────────────────────────────

    internal RECT? ResolveZoneRect(AppItem item, AppConfig config)
    {
        if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) return null;

        string uuid = item.FancyzoneUuid.ToLowerInvariant().Trim('{', '}');
        if (!config.FzLayoutsCache.TryGetValue(uuid, out var cacheEntry)) return null;

        int zoneIdx = ParseZoneIndex(item.Fancyzone);
        var workArea = GetMonitorWorkArea(item.Monitor);
        if (workArea == null) return null;

        var layoutInfo = ParseLayoutInfo(cacheEntry);
        if (layoutInfo == null) return null;

        return ZoneCalculator.CalculateZoneRect(layoutInfo, zoneIdx, workArea.Value);
    }

    internal static int ParseZoneIndex(string zoneName)
    {
        int idx = zoneName.LastIndexOf("Zona ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && int.TryParse(zoneName[(idx + 5)..].Trim(), out int n))
            return n - 1;
        return 0;
    }

    internal static RECT? GetMonitorWorkArea(string monitorLabel)
    {
        var monitors = WindowManager.GetMonitors();
        if (monitors.Count == 0) return null;

        var match = System.Text.RegularExpressions.Regex.Match(monitorLabel, @"Pantalla\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
        {
            if (idx >= 1 && idx <= monitors.Count)
                return monitors[idx - 1].WorkArea;
        }

        foreach (var m in monitors)
        {
            if (monitorLabel.Contains($"[{m.Name}]", StringComparison.OrdinalIgnoreCase))
                return m.WorkArea;
        }

        return monitors[0].WorkArea;
    }

    internal static LayoutInfo? ParseLayoutInfo(Config.LayoutCacheEntry entry)
    {
        try
        {
            var info = entry.Info;
            return new LayoutInfo
            {
                Type = entry.Type,
                Rows = info.TryGetProperty("rows", out var r) ? r.GetInt32() : 1,
                Columns = info.TryGetProperty("columns", out var c) ? c.GetInt32() : 1,
                RowsPercentage = info.TryGetProperty("rows-percentage", out var rp) ? rp.Deserialize<int[]>() : null,
                ColumnsPercentage = info.TryGetProperty("columns-percentage", out var cp) ? cp.Deserialize<int[]>() : null,
                CellChildMap = info.TryGetProperty("cell-child-map", out var cm) ? cm.Deserialize<int[][]>() : null,
                ShowSpacing = info.TryGetProperty("show-spacing", out var ss) && ss.GetBoolean(),
                Spacing = info.TryGetProperty("spacing", out var sp) ? sp.GetInt32() : 0,
            };
        }
        catch { return null; }
    }

    internal static bool TryParseDesktopIndex(string name, out int index)
    {
        index = 0;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[^1], out index);
    }

    private static string GetItemDisplayName(AppItem item)
    {
        try
        {
            return item.Type switch
            {
                "exe" => Path.GetFileNameWithoutExtension(item.Path),
                "url" => new Uri(item.Path).Host,
                "ide" => $"{item.IdeCmd}: {Path.GetFileName(item.Path)}",
                "vscode" => $"VSCode: {Path.GetFileName(item.Path)}",
                "powershell" => $"Terminal: {Path.GetFileName(item.Path)}",
                "obsidian" => $"Obsidian: {Path.GetFileName(item.Path)}",
                _ => item.Path
            };
        }
        catch { return item.Path ?? "Unknown"; }
    }

    private void Report(string msg, int percent)
    {
        Console.WriteLine($"[Orchestrator] {percent}% {msg}");
        ProgressChanged?.Invoke(msg, percent);
    }
}
