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

        Report($"Iniciando workspace (Motor de Convergencia): {category}", 0);
        ZoneStack.Instance.Clear();

        // Auto-adapt monitor configuration if opening in a different environment
        WorkspaceResolver.ResolveEnvironment(items);

        // Phase 0: Ensure Virtual Desktops exist first (so GUIDs are available for FZ sync)
        await EnsureVirtualDesktopsAsync(items);

        // Phase 1a: Sync FancyZones layouts
        Report("Sincronizando layouts...", 5);
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);

        // --- CONVERGENCE ENGINE: Desktop-Sequential / App-Parallel Pulse ---
        // We visit each desktop once, launch its apps in parallel, and stabilize them
        // before moving to the next one. This prevents "mixing" windows on the same screen.
        int total = items.Count;
        var groups = items
            .GroupBy(i => i.Desktop ?? "Por defecto")
            .OrderBy(g => g.Key == "Por defecto" ? 999 : TryParseDesktopIndex(g.Key, out int n) ? n : 999)
            .ToList();

        var hwndsByItem = new Dictionary<AppItem, nint>();
        int processedCount = 0;
        foreach (var group in groups)
        {
            string desktopName = group.Key;
            Report($"Escritorio: {desktopName}", 10 + (int)((double)processedCount / total * 80));

            // 1. Switch to the target desktop and WAIT for OS confirmation
            if (!await PrepareDesktopAsync(desktopName))
            {
                Logger.Error($"[Orchestrator] Saltando escritorio {desktopName} por error en cambio.");
                processedCount += group.Count();
                continue;
            }

            // 2. Process windows for THIS desktop ONE BY ONE
            foreach (var item in group)
            {
                processedCount++;
                Report($"Abriendo: {GetItemDisplayName(item)}", 10 + (int)((double)processedCount / total * 80));

                if (int.TryParse(item.Delay, out int d) && d > 0)
                    await Task.Delay(d);

                // Launch and WAIT for the window to appear
                nint hwnd = await LaunchOnlyAsync(item);

                if (hwnd != 0)
                {
                    // Track the HWND for reliable integrity sweeps later
                    hwndsByItem[item] = hwnd;

                    // ── APP READINESS WAIT ──
                    // The window handle exists but the app may still be loading.
                    // Browsers render pages, VSCode loads extensions, Obsidian opens vaults —
                    // if we snap before they finish initializing they override our position.
                    int readiness = GetReadinessDelay(item);
                    if (readiness > 0) await Task.Delay(readiness);

                    // ── CRITICAL ISOLATION SAFEGUARD ──
                    // Avoid checking against 'current' desktop, as the user might have switched manually.
                    // We check if the window is on the SPECIFIC target desktop.
                    var targetId = ResolveDesktopGuid(item);
                    if (targetId.HasValue && targetId != Guid.Empty)
                    {
                        var currentDeskId = VirtualDesktopManager.Instance.GetWindowDesktopId(hwnd);
                        if (currentDeskId.HasValue && currentDeskId != targetId)
                        {
                            Logger.Warn($"[Orchestrator] {GetItemDisplayName(item)} apareció en escritorio {currentDeskId}, pero debería estar en {targetId}. Moviendo...");
                            VirtualDesktopManager.Instance.MoveWindowToDesktop(hwnd, targetId.Value);

                            // Give the OS time to verify the move
                            await Task.Delay(500);
                        }
                    }

                    // Strict Snap: Position the window
                    RECT? zoneRect = ResolveZoneRect(item, config);
                    if (zoneRect.HasValue)
                    {
                        Logger.Info($"[Orchestrator] Ajustando {GetItemDisplayName(item)} en {desktopName}...");

                        // ── SMART SILENCE ──
                        // If the window is NOT on the current view, we use silent mode.
                        // This prevents the SWP_SHOWWINDOW flag from forcing the OS to switch desktops.
                        bool isHere = VirtualDesktopManager.Instance.IsWindowOnCurrentDesktop(hwnd);
                        bool ok = await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value, retries: 4, silent: !isHere);

                        if (ok) RegisterInZoneStack(item, hwnd, config);
                    }

                    await Task.Delay(250); // Brief pause between items
                }
                else
                {
                    Logger.Warn($"[Orchestrator] Tiempo de espera agotado para {GetItemDisplayName(item)}");
                }
            }

            await Task.Delay(500); // Breathe between desktops
        }

        // --- PHASE 3: Global Integrity Sweep ---
        // Three passes with increasing strictness. Handles apps that:
        //   - Were still animating when we first snapped
        //   - Resized themselves after loading content (browsers, IDEs)
        //   - Were moved by the user during launch
        Report("Auditoría final de integridad global...", 95);
        await Task.Delay(800);
        await FinalIntegritySweep(items, config, silent: true,  trackedHwnds: hwndsByItem);
        await Task.Delay(1200);
        await FinalIntegritySweep(items, config, silent: true,  trackedHwnds: hwndsByItem);
        await Task.Delay(2000);
        // Third pass: slower apps (browsers, VSCode, IDEs) should have finished loading by now
        await FinalIntegritySweep(items, config, silent: false, trackedHwnds: hwndsByItem);

        Report("Workspace estabilizado correctamente.", 100);
        Core.SystemTray.TrayManager.Instance?.ShowBalloon(
            "Workspace lanzado",
            $"'{category}' iniciado correctamente.",
            timeoutMs: 3000);
    }

    private async Task<bool> PrepareDesktopAsync(string desktopLabel)
    {
        var vdm = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();

        if (desktopLabel != "Por defecto" && TryParseDesktopIndex(desktopLabel, out int dIdx))
        {
            if (dIdx - 1 < desktops.Count)
            {
                Guid targetId = desktops[dIdx - 1];
                var current = vdm.GetCurrentDesktopId();
                if (current == targetId) return true;

                Logger.Info($"[Orchestrator] Cambiando al escritorio {desktopLabel} ({targetId})...");
                vdm.SwitchToDesktop(targetId);
                
                // Aggressive Verification Loop
                bool success = false;
                for (int i = 0; i < 5; i++)
                {
                    success = await vdm.WaitForDesktopSwitchAsync(targetId, timeoutMs: 800);
                    if (success) break;
                    
                    Logger.Warn($"[Orchestrator] Reintentando cambio de escritorio ({i + 1}/5)...");
                    vdm.SwitchToDesktop(targetId);
                }

                if (!success)
                {
                    Logger.Error($"[Orchestrator] ERROR: No se pudo confirmar cambio al escritorio {desktopLabel} tras varios intentos.");
                    return false;
                }
                return true; 
            }
        }
        return true;
    }

    private async Task<nint> LaunchOnlyAsync(AppItem item)
    {
        var before = new HashSet<nint>(WindowManager.GetVisibleWindows());
        var process = ProcessLauncher.Launch(item);
        nint hwnd = 0;

        // Type-specific timeout: slow apps (VSCode, IDE, Obsidian) may take longer to open
        int timeout = GetDetectionTimeout(item);

        if (process != null)
        {
            try { hwnd = await WindowDetector.WaitForWindowAsync(process, timeoutMs: timeout); } catch { }
        }

        if (hwnd == 0)
        {
            hwnd = await WindowDetector.WaitForNewWindowAsync(
                before, typeHint: item.Type, pathHint: item.Path, timeoutMs: timeout);
        }

        // --- Convergence Fallback ---
        // If still no window, look at ALL visible windows and use the scoring system (like Restore mode)
        if (hwnd == 0)
        {
            var all = WindowManager.GetVisibleWindows();
            hwnd = WindowDetector.ScoreMatchBestWindow(item, all.ToList());
            if (hwnd != 0) Logger.Info($"[Orchestrator] Ventana para {GetItemDisplayName(item)} encontrada vía fallback de scoring.");
        }

        return hwnd;
    }

    /// <summary>
    /// Time to wait after window detection before snapping.
    /// Apps continue loading after their window handle appears — this gives them time
    /// to finish so they don't override our position.
    /// </summary>
    private static int GetReadinessDelay(AppItem item) => item.Type switch
    {
        "url"        => 650,  // Browser: page render + layout stabilization
        "vscode"     => 550,  // VSCode: extension activation + workspace load
        "ide"        => 550,  // Other IDEs: project indexing
        "obsidian"   => 500,  // Obsidian: vault open + plugin load
        "powershell" => 200,  // Terminal: starts fast
        "exe"        => 150,  // Generic executable
        _            => 150
    };

    /// <summary>
    /// How long to wait for a window to appear after launch.
    /// Heavy apps need more time, especially on slow machines or first-launch.
    /// </summary>
    private static int GetDetectionTimeout(AppItem item) => item.Type switch
    {
        "vscode"     => 14_000, // VSCode: slow on first launch or large workspace
        "ide"        => 14_000, // IDEs: project scanning
        "url"        => 12_000, // Browser: may need to start cold
        "obsidian"   => 12_000, // Obsidian: vault loading
        "powershell" => 8_000,
        "exe"        => 8_000,
        _            => 8_000
    };

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

        // Check and remap monitors if changed
        WorkspaceResolver.ResolveEnvironment(items);

        FancyZonesReader.SyncCacheFromDisk();
        LayoutSyncer.SyncForWorkspace(items, config.AppliedMappings);
        await EnsureVirtualDesktopsAsync(items);

        // Collect all visible windows, excluding system processes
        var allWindows = WindowManager.GetVisibleWindows()
            .Where(h => !IsSystemWindow(h))
            .ToList();

        int total = items.Count;
        int matched = 0;
        var hwndsByItem = new Dictionary<AppItem, nint>();

        for (int i = 0; i < total; i++)
        {
            var item = items[i];
            Report($"Buscando: {GetItemDisplayName(item)}", 10 + (int)((double)(i + 1) / total * 80));

            // PHASE 6: Use the scoring system to find the best match
            nint hwnd = WindowDetector.ScoreMatchBestWindow(item, allWindows);
            if (hwnd == 0) continue;

            hwndsByItem[item] = hwnd;

            // Move to target desktop if needed (verify first to avoid unnecessary desktop switches)
            var vdm = VirtualDesktopManager.Instance;
            var desktops = vdm.GetDesktops();
            if (item.Desktop != "Por defecto" && TryParseDesktopIndex(item.Desktop, out int dIdx))
            {
                if (dIdx - 1 < desktops.Count)
                {
                    var targetDesk = desktops[dIdx - 1];
                    var actualDesk = vdm.GetWindowDesktopId(hwnd);
                    if (actualDesk.HasValue && actualDesk != targetDesk)
                    {
                        vdm.MoveWindowToDesktop(hwnd, targetDesk);
                        await Task.Delay(300);
                    }
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

        // Final sweep — uses tracked HWNDs (no re-scoring needed) + visual drift detection
        Report("Verificando integridad final...", 95);
        await Task.Delay(800);
        await FinalIntegritySweep(items, config, trackedHwnds: hwndsByItem);
        await Task.Delay(1200);
        await FinalIntegritySweep(items, config, trackedHwnds: hwndsByItem);

        string restoreMsg = matched == total
            ? $"'{category}' restaurado: {matched}/{total} ventanas."
            : $"'{category}': {matched}/{total} ventanas restauradas. {total - matched} no encontradas.";
        Report(restoreMsg, 100);
        Core.SystemTray.TrayManager.Instance?.ShowBalloon(
            matched == total ? "Workspace restaurado" : "Restauración parcial",
            restoreMsg,
            timeoutMs: 4000);
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
            Console.WriteLine($"[Orchestrator] Snapping '{GetItemDisplayName(item)}' to zone...");
            await DwmHelper.ApplyZoneRect(hwnd, zoneRect.Value);
        }
        else
        {
            Console.WriteLine($"[Orchestrator] No zone assigned (or layout missing) for '{GetItemDisplayName(item)}'");
        }

        return hwnd;
    }

    // ── PHASE 7: Final integrity sweep ───────────────────────────────────────

    /// <summary>
    /// Verifies that every workspace window is on the correct desktop and in the correct zone.
    /// Uses tracked HWNDs from the launch phase when available (more reliable than re-scoring).
    /// Falls back to the scoring system for windows that weren't tracked.
    ///
    /// IMPORTANT: Uses DWM visual bounds (DWMWA_EXTENDED_FRAME_BOUNDS) for drift comparison,
    /// NOT GetWindowRect. GetWindowRect returns logical coords that include the invisible
    /// DWM shadow border (~8px per side), which would cause false drift detections.
    /// </summary>
    private async Task FinalIntegritySweep(
        List<AppItem> items,
        AppConfig config,
        bool silent = false,
        Dictionary<AppItem, nint>? trackedHwnds = null)
    {
        var currentWindows = WindowManager.GetVisibleWindows().ToList();
        var vdm = VirtualDesktopManager.Instance;

        // Two internal passes per sweep call: first lenient, second strict
        for (int pass = 1; pass <= 2; pass++)
        {
            Logger.Info($"[Orchestrator] Integridad (silent={silent}): Pase {pass}/2...");

            foreach (var item in items)
            {
                if (item.Fancyzone == "Ninguna" || string.IsNullOrEmpty(item.FancyzoneUuid)) continue;

                RECT? zoneRect = ResolveZoneRect(item, config);
                if (!zoneRect.HasValue) continue;

                // Prefer tracked HWND (from launch phase) — it's more reliable than scoring.
                // Only fall back to scoring if no tracked HWND exists or the window was closed.
                nint hwnd = 0;
                if (trackedHwnds != null &&
                    trackedHwnds.TryGetValue(item, out nint tracked) &&
                    tracked != 0 &&
                    User32.IsWindow(tracked))
                {
                    hwnd = tracked;
                }
                else
                {
                    hwnd = WindowDetector.ScoreMatchBestWindow(item, currentWindows);
                }

                if (hwnd == 0) continue;

                // 1. Verify Desktop placement
                Guid? targetDesktopId = ResolveDesktopGuid(item);
                if (targetDesktopId.HasValue && targetDesktopId != Guid.Empty)
                {
                    var actualDesk = vdm.GetWindowDesktopId(hwnd);
                    if (actualDesk.HasValue && actualDesk != targetDesktopId)
                    {
                        Logger.Warn($"[Orchestrator] Integridad: Corrigiendo escritorio de '{GetItemDisplayName(item)}' ({actualDesk} -> {targetDesktopId})");
                        vdm.MoveWindowToDesktop(hwnd, targetDesktopId.Value);
                        await Task.Delay(200);
                    }
                }

                // 2. Verify Position using VISUAL bounds (not logical / shadow-inclusive bounds).
                //    GetWindowRect returns logical coords which include the ~8px DWM shadow per side.
                //    Comparing logical vs. our visual zone target would cause false drift detections
                //    (logical.Width ≈ visual.Width + 16, always exceeding tight tolerances).
                RECT actual = DwmHelper.GetVisualBounds(hwnd);
                var  target = zoneRect.Value;

                // Tolerance: lenient on pass 1 (catches real drift), tight on pass 2 (fine-tune)
                int tolerance = pass == 2 ? 8 : 30;

                bool hasDrifted =
                    Math.Abs(actual.Left   - target.Left)   > tolerance ||
                    Math.Abs(actual.Top    - target.Top)    > tolerance ||
                    Math.Abs(actual.Width  - target.Width)  > tolerance ||
                    Math.Abs(actual.Height - target.Height) > tolerance;

                if (hasDrifted)
                {
                    Logger.Info($"[Orchestrator] Integridad: Re-ajuste de '{GetItemDisplayName(item)}'" +
                                $" — actual [{actual.Left},{actual.Top} {actual.Width}x{actual.Height}]" +
                                $" → target [{target.Left},{target.Top} {target.Width}x{target.Height}]");
                    bool ok = await DwmHelper.ApplyZoneRect(hwnd, target, retries: 3, silent: silent);
                    if (ok) RegisterInZoneStack(item, hwnd, config);
                }
            }

            if (pass == 1) await Task.Delay(400);
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

    internal static Guid? ResolveDesktopGuid(AppItem item)
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

        // FancyZones stores per-monitor-desktop spacing overrides in applied-layouts.json.
        // These can differ from the base spacing in custom-layouts.json (cache).
        // Using the wrong spacing produces zones that don't match FancyZones' visual overlays.
        var appliedEntry = GetAppliedLayoutEntry(item, uuid);
        if (appliedEntry != null && appliedEntry.Spacing >= 0)
        {
            if (layoutInfo.Spacing != appliedEntry.Spacing || layoutInfo.ShowSpacing != appliedEntry.ShowSpacing)
            {
                Console.WriteLine($"[Orchestrator] Spacing override for '{uuid}': cache={layoutInfo.Spacing}(show={layoutInfo.ShowSpacing}) → applied={appliedEntry.Spacing}(show={appliedEntry.ShowSpacing})");
                layoutInfo.Spacing = appliedEntry.Spacing;
                layoutInfo.ShowSpacing = appliedEntry.ShowSpacing;
            }
        }

        var zoneRect = ZoneCalculator.CalculateZoneRect(layoutInfo, zoneIdx, workArea.Value);
        if (zoneRect.HasValue)
            Console.WriteLine($"[Orchestrator] ZoneRect for '{item.Fancyzone}' on monitor '{item.Monitor}': [{zoneRect.Value.Left},{zoneRect.Value.Top},{zoneRect.Value.Right},{zoneRect.Value.Bottom}] Size={zoneRect.Value.Width}x{zoneRect.Value.Height}");
        return zoneRect;
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
                RowsPercentage    = info.TryGetProperty("rows-percentage",    out var rp) ? rp.Deserialize<int[]>() : new[] { 10000 },
                ColumnsPercentage = info.TryGetProperty("columns-percentage", out var cp) ? cp.Deserialize<int[]>() : new[] { 10000 },
                CellChildMap      = info.TryGetProperty("cell-child-map",     out var cm) ? cm.Deserialize<int[][]>() : new[] { new[] { 0 } },
                // Python defaults show-spacing=True; only disable if explicitly false
                ShowSpacing       = !info.TryGetProperty("show-spacing", out var ss) || ss.GetBoolean(),
                Spacing           = info.TryGetProperty("spacing", out var sp) ? sp.GetInt32() : 0,
                // Canvas layout fields (missing previously — broke all canvas zone snapping)
                ReferenceWidth    = info.TryGetProperty("ref-width",  out var rw) ? rw.GetDouble() : 0,
                ReferenceHeight   = info.TryGetProperty("ref-height", out var rh) ? rh.GetDouble() : 0,
                CanvasZones       = info.TryGetProperty("zones", out var zs)
                    ? zs.Deserialize<CanvasZoneInfo[]>()
                    : null,
            };
        }
        catch { return null; }
    }

    internal static bool TryParseDesktopIndex(string name, out int index)
    {
        index = 0;
        if (string.IsNullOrEmpty(name)) return false;

        // Try direct parse (e.g. "1")
        if (int.TryParse(name, out index)) return true;

        // Try segment parse (e.g. "Escritorio 1")
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && int.TryParse(parts[^1], out index)) return true;

        return false;
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

    /// <summary>
    /// Finds the applied-layouts.json entry for the given item and layout UUID,
    /// to read per-monitor-desktop spacing overrides that FancyZones applies
    /// on top of the base layout definition.
    /// </summary>
    private static AppliedLayoutEntry? GetAppliedLayoutEntry(AppItem item, string layoutUuid)
    {
        try
        {
            var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
            var monitors = MonitorManager.GetActiveMonitors();

            // Find the monitor that matches this item's monitor label
            MonitorInfo? mon = null;
            if (!string.IsNullOrEmpty(item.Monitor) && item.Monitor != "Por defecto")
            {
                mon = monitors.FirstOrDefault(m =>
                    m.PtName == item.Monitor || m.Name == item.Monitor ||
                    m.Name.StartsWith(item.Monitor, StringComparison.OrdinalIgnoreCase) ||
                    item.Monitor.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase) ||
                    m.PtInstance == item.Monitor);
            }

            // Resolve the target desktop GUID string
            string? desktopGuidStr = null;
            var desktopId = ResolveDesktopGuid(item);
            if (desktopId.HasValue && desktopId != Guid.Empty)
                desktopGuidStr = desktopId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();

            // Find the best matching applied-layout entry
            foreach (var entry in appliedLayouts)
            {
                if (!entry.LayoutUuid.Equals(layoutUuid, StringComparison.OrdinalIgnoreCase)) continue;

                bool monMatch = mon == null
                    || entry.MonitorName == mon.PtName
                    || entry.Instance == mon.PtInstance;
                if (!monMatch) continue;

                bool deskMatch = desktopGuidStr == null
                    || entry.DesktopId.Equals(desktopGuidStr, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(entry.DesktopId);

                if (monMatch && deskMatch) return entry;
            }

            // Fallback: any entry for this monitor (ignore desktop)
            return appliedLayouts.FirstOrDefault(e =>
                e.LayoutUuid.Equals(layoutUuid, StringComparison.OrdinalIgnoreCase) &&
                (mon == null || e.MonitorName == mon.PtName || e.Instance == mon.PtInstance));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Orchestrator] GetAppliedLayoutEntry error: {ex.Message}");
            return null;
        }
    }
}
