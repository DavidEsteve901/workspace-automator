using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.ZoneEngine;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Main workspace launch orchestrator.
/// Implements all 7 phases from the Python script.
/// </summary>
public sealed class WorkspaceOrchestrator
{
    public static readonly WorkspaceOrchestrator Instance = new();

    public event Action<string, int>? ProgressChanged;

    private WorkspaceOrchestrator() { }

    // ── PHASE 1+2+3+4: Full Launch ───────────────────────────────────────────

    public async Task LaunchWorkspaceAsync(string category)
    {
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items) || items.Count == 0)
        {
            Report("No items in workspace.", 0);
            return;
        }

        Report($"Iniciando workspace: {category}", 0);
        ZoneStack.Instance.Clear();

        // PHASE 1a: Sync FancyZones layouts before anything
        Report("Sincronizando layouts FancyZones...", 5);
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);

        // PHASE 1b: Ensure virtual desktops exist
        Report("Verificando escritorios virtuales...", 10);
        await EnsureVirtualDesktopsAsync(items);

        // PHASE 1c: Group items by target desktop for correct launch ordering
        // Items without a desktop go to "Por defecto" group (launched on current desktop)
        var groups = items
            .GroupBy(i => i.Desktop ?? "Por defecto")
            .OrderBy(g => g.Key == "Por defecto" ? 999 : TryParseDesktopIndex(g.Key, out int n) ? n : 999)
            .ToList();

        int total = items.Count;
        int launched = 0;

        foreach (var group in groups)
        {
            // PHASE 1c: Switch to the target desktop and let the OS settle
            var vdm = VirtualDesktopManager.Instance;
            var desktops = vdm.GetDesktops();

            if (group.Key != "Por defecto" && TryParseDesktopIndex(group.Key, out int dIdx))
            {
                if (dIdx - 1 < desktops.Count)
                {
                    Report($"Cambiando a {group.Key}...", 10 + (int)((double)launched / total * 70));
                    vdm.SwitchToDesktop(desktops[dIdx - 1]);
                    await Task.Delay(500); // Let the OS settle the desktop switch
                }
            }

            // Launch each item in this desktop group
            foreach (var item in group)
            {
                launched++;
                int percent = 15 + (int)((double)launched / total * 70);
                Report($"Lanzando: {GetItemDisplayName(item)}", percent);

                if (int.TryParse(item.Delay, out int delayMs) && delayMs > 0)
                    await Task.Delay(delayMs);

                nint hwnd = await LaunchAndSnapAsync(item, config);
                if (hwnd != 0)
                    RegisterInZoneStack(item, hwnd, config);
            }
        }

        // PHASE 7: Final integrity sweep — wait for OS to settle, then re-snap any drifted windows
        Report("Barrido de integridad final...", 90);
        await Task.Delay(2000);
        await FinalIntegritySweep(items, config);

        Report("Workspace lanzado correctamente.", 100);
    }

    // ── PHASE 6: Recovery Mode ───────────────────────────────────────────────

    /// <summary>
    /// Restore workspace: find already-open windows using the scoring system and snap them.
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
        FancyZonesReader.SyncCacheFromDisk();
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);
        await EnsureVirtualDesktopsAsync(items);

        // Collect all visible windows, excluding system processes
        var allWindows = WindowManager.GetVisibleWindows()
            .Where(h => !IsSystemWindow(h))
            .ToList();

        int total = items.Count;
        int matched = 0;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            Report($"Buscando: {GetItemDisplayName(item)}", 10 + (int)((double)(i + 1) / total * 80));

            // PHASE 6: Use the scoring system to find the best match
            nint hwnd = WindowDetector.ScoreMatchBestWindow(item, allWindows);
            if (hwnd == 0) continue;

            // Switch to target desktop if needed
            var vdm = VirtualDesktopManager.Instance;
            var desktops = vdm.GetDesktops();
            if (item.Desktop != "Por defecto" && TryParseDesktopIndex(item.Desktop, out int dIdx))
            {
                if (dIdx - 1 < desktops.Count)
                {
                    vdm.SwitchToDesktop(desktops[dIdx - 1]);
                    await Task.Delay(300);
                }
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
        var allWindows = WindowManager.GetVisibleWindows().ToList();
        int total = items.Count;
        int closed = 0;

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            Report($"Cerrando: {GetItemDisplayName(item)}", 10 + (int)((double)(i + 1) / total * 80));

            nint hwnd = WindowDetector.ScoreMatchBestWindow(item, allWindows);
            if (hwnd == 0) continue;

            try
            {
                User32.SendMessage(hwnd, User32.WM_CLOSE, 0, 0);
                ZoneStack.Instance.Unregister(hwnd);
                closed++;
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestrator] Error closing window: {ex.Message}");
            }
        }

        ZoneStack.Instance.Clear();
        Report($"Workspace limpiado: {closed} ventanas cerradas.", 100);
    }

    // ── PHASE 3+4: Launch and snap individual item ───────────────────────────

    private async Task<nint> LaunchAndSnapAsync(AppItem item, AppConfig config)
    {
        // Snapshot before launch to detect new windows (Phase 3 fallback)
        var before = new HashSet<nint>(WindowManager.GetVisibleWindows());

        var process = ProcessLauncher.Launch(item);

        nint hwnd = 0;

        // PHASE 3 - Priority 1: PID-based detection
        if (process != null)
        {
            try
            {
                hwnd = await WindowDetector.WaitForWindowAsync(process, timeoutMs: 8_000);
            }
            catch { }
        }

        // PHASE 3 - Priority 2: Heuristic new-window detection with keywords
        if (hwnd == 0)
        {
            hwnd = await WindowDetector.WaitForNewWindowAsync(
                before,
                typeHint: item.Type,
                pathHint: item.Path,
                timeoutMs: 8_000);
        }

        if (hwnd == 0)
        {
            Console.WriteLine($"[Orchestrator] Could not find window for {item.Path}");
            return 0;
        }

        // PHASE 4: Snap to zone with DWM compensation + force focus
        RECT? zoneRect = ResolveZoneRect(item, config);
        if (zoneRect.HasValue)
        {
            await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
        }

        return hwnd;
    }

    // ── PHASE 7: Final integrity sweep ───────────────────────────────────────

    private async Task FinalIntegritySweep(List<AppItem> items, AppConfig config)
    {
        var currentWindows = WindowManager.GetVisibleWindows().ToList();

        foreach (var item in items)
        {
            if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) continue;

            RECT? zoneRect = ResolveZoneRect(item, config);
            if (!zoneRect.HasValue) continue;

            // Use scoring to find the window even if hwnd changed
            nint hwnd = WindowDetector.ScoreMatchBestWindow(item, currentWindows);
            if (hwnd == 0) continue;

            // Verify using visual bounds (DWM)
            RECT actual = WindowManager.GetWindowRect(hwnd);
            var target = zoneRect.Value;

            // PHASE 7: Re-snap if the window has drifted significantly (>= 50px as per Python script)
            // This handles cases where apps auto-resize or shift after being snapped.
            int drift = Math.Abs(actual.Left - target.Left)
                      + Math.Abs(actual.Top - target.Top)
                      + Math.Abs(actual.Width - target.Width)
                      + Math.Abs(actual.Height - target.Height);

            if (drift >= 50)
            {
                Console.WriteLine($"[Orchestrator] Integridad: Re-snapping window (drift={drift}px): {GetItemDisplayName(item)}");
                await DwmHelper.ApplyZoneRect(hwnd, target, retries: 2);
                RegisterInZoneStack(item, hwnd, config);
            }
        }
    }

    // ── Zone Stack Registration ──────────────────────────────────────────────

    private void RegisterInZoneStack(AppItem item, nint hwnd, AppConfig config)
    {
        if (string.IsNullOrEmpty(item.FancyzoneUuid) || item.Fancyzone == "Ninguna") return;

        string layoutUuid = item.FancyzoneUuid.Trim('{', '}').ToLowerInvariant();
        int zoneIdx       = ParseZoneIndex(item.Fancyzone);
        Guid desktopGuid  = ResolveDesktopGuid(item) ?? Guid.Empty;
        string monitorId  = ResolveMonitorPtInstance(item.Monitor);

        if (desktopGuid == Guid.Empty)
            desktopGuid = VirtualDesktopManager.Instance.GetCurrentDesktopId() ?? Guid.Empty;

        var zoneKey = new ZoneStack.ZoneKey(desktopGuid, monitorId, layoutUuid, zoneIdx);
        ZoneStack.Instance.Register(zoneKey, hwnd);
        Console.WriteLine($"[Orchestrator] Registered hwnd {hwnd}: desktop={desktopGuid:N} monitor={monitorId} layout={layoutUuid} zone={zoneIdx}");
    }

    private static string ResolveMonitorPtInstance(string? monitorLabel)
    {
        if (string.IsNullOrEmpty(monitorLabel) || monitorLabel == "Por defecto") return "default";

        // Try to find the active monitor whose label matches what was stored
        var monitors = MonitorManager.GetActiveMonitors();
        var match = monitors.FirstOrDefault(m =>
            m.Name == monitorLabel ||
            m.PtInstance == monitorLabel ||
            m.PtName == monitorLabel ||
            m.Name.StartsWith(monitorLabel, StringComparison.OrdinalIgnoreCase));

        return match?.PtInstance ?? monitorLabel;
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

    // ── Legacy FindWindowForItem (used by old RestoreWorkspace path) ──────────

    internal nint FindWindowForItem(AppItem item, List<nint> windows)
        => WindowDetector.ScoreMatchBestWindow(item, windows);

    // ── Virtual desktop management ───────────────────────────────────────────

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

    // ── Zone rect resolution ─────────────────────────────────────────────────

    internal RECT? ResolveZoneRect(AppItem item, AppConfig config)
    {
        if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) return null;

        string uuid = item.FancyzoneUuid.ToLowerInvariant().Trim('{', '}');
        if (!config.FzLayoutsCache.TryGetValue(uuid, out var cacheEntry)) return null;

        int zoneIdx  = ParseZoneIndex(item.Fancyzone);
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

    /// <summary>
    /// Resolves the monitor work area for an item. Handles both legacy "Pantalla N" format
    /// and the new format where the label is the monitor's Name (e.g. "SDC41B6 (1646x1029) ★").
    /// </summary>
    internal static RECT? GetMonitorWorkArea(string? monitorLabel)
    {
        var monitorInfos = MonitorManager.GetActiveMonitors();
        if (monitorInfos.Count == 0) return null;

        if (string.IsNullOrEmpty(monitorLabel) || monitorLabel == "Por defecto")
            return monitorInfos.FirstOrDefault(m => m.IsPrimary)?.WorkArea
                ?? monitorInfos[0].WorkArea;

        // 1. Try exact name match
        var exact = monitorInfos.FirstOrDefault(m =>
            m.Name == monitorLabel ||
            m.Name.Equals(monitorLabel, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.WorkArea;

        // 2. Try partial name match (starts-with / contains)
        var partial = monitorInfos.FirstOrDefault(m =>
            m.Name.StartsWith(monitorLabel, StringComparison.OrdinalIgnoreCase) ||
            monitorLabel.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase));
        if (partial != null) return partial.WorkArea;

        // 3. Try PtName / PtInstance match
        var ptMatch = monitorInfos.FirstOrDefault(m =>
            m.PtName == monitorLabel || m.PtInstance == monitorLabel);
        if (ptMatch != null) return ptMatch.WorkArea;

        // 4. Legacy "Pantalla N" format
        var regexMatch = System.Text.RegularExpressions.Regex.Match(monitorLabel, @"Pantalla\s+(\d+)");
        if (regexMatch.Success && int.TryParse(regexMatch.Groups[1].Value, out int idx))
        {
            if (idx >= 1 && idx <= monitorInfos.Count)
                return monitorInfos[idx - 1].WorkArea;
        }

        // 5. Fallback to primary
        Console.WriteLine($"[Orchestrator] Monitor label '{monitorLabel}' not found, using primary.");
        return monitorInfos.FirstOrDefault(m => m.IsPrimary)?.WorkArea ?? monitorInfos[0].WorkArea;
    }

    internal static LayoutInfo? ParseLayoutInfo(Config.LayoutCacheEntry entry)
    {
        try
        {
            var info = entry.Info;
            return new LayoutInfo
            {
                Type              = entry.Type,
                Rows              = info.TryGetProperty("rows", out var r)   ? r.GetInt32()  : 1,
                Columns           = info.TryGetProperty("columns", out var c) ? c.GetInt32() : 1,
                RowsPercentage    = info.TryGetProperty("rows-percentage",    out var rp) ? rp.Deserialize<int[]>() : null,
                ColumnsPercentage = info.TryGetProperty("columns-percentage", out var cp) ? cp.Deserialize<int[]>() : null,
                CellChildMap      = info.TryGetProperty("cell-child-map",     out var cm) ? cm.Deserialize<int[][]>() : null,
                ShowSpacing       = info.TryGetProperty("show-spacing", out var ss) && ss.GetBoolean(),
                Spacing           = info.TryGetProperty("spacing", out var sp) ? sp.GetInt32() : 0,
            };
        }
        catch { return null; }
    }

    internal static bool TryParseDesktopIndex(string name, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(name)) return false;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[^1], out index);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsSystemWindow(nint hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string name = proc.ProcessName.ToLowerInvariant();
            return name is "explorer" or "taskmgr" or "searchhost" or "shellexperiencehost"
                        or "startmenuexperiencehost" or "systemsettings" or "dwm" or "sihost"
                        or "winlogon" or "ctfmon" or "applicationframehost";
        }
        catch { return false; }
    }

    private static string GetItemDisplayName(AppItem item)
    {
        try
        {
            return item.Type switch
            {
                "exe"        => Path.GetFileNameWithoutExtension(item.Path),
                "url"        => new Uri(item.Path).Host,
                "ide"        => $"{item.IdeCmd}: {Path.GetFileName(item.Path)}",
                "vscode"     => $"VSCode: {Path.GetFileName(item.Path)}",
                "powershell" => $"Terminal: {Path.GetFileName(item.Path)}",
                "obsidian"   => $"Obsidian: {Path.GetFileName(item.Path)}",
                _            => item.Path
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
