using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.FancyZones;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.NativeInterop;
using WorkspaceLauncher.Core.Utils;
using WorkspaceLauncher.Core.CustomZoneEngine.Models;
using WorkspaceLauncher.Core.ZoneEngine;

namespace WorkspaceLauncher.Bridge;

/// <summary>
/// Bidirectional JSON bridge between WebView2 (React) and C# backend.
///
/// JS → C#: chrome.webview.postMessage({ action: "...", payload: {...}, requestId: "..." })
/// C# → JS: CoreWebView2.PostWebMessageAsJson(JSON)
///
/// For fire-and-forget actions: JS sends { action, payload }.
/// For request/response (invoke): JS sends { action, payload, requestId },
///   C# responds with { event: "invoke_response", data: { requestId, result } }.
/// </summary>
[ComVisible(true)]
public sealed class WebBridge
{
    private readonly CoreWebView2 _webView;
    private readonly System.Windows.Window _window;

    // ── Broadcast registry (all active bridge instances, weak refs) ───────────
    private static readonly List<WeakReference<WebBridge>> _allBridges = [];
    private static readonly object _broadcastLock = new();

    public static void BroadcastCzeState(string state)
    {
        lock (_broadcastLock)
        {
            _allBridges.RemoveAll(wr => !wr.TryGetTarget(out _));
            foreach (var wr in _allBridges)
                if (wr.TryGetTarget(out var b))
                    b.SendEvent("cze_state_changed", new { state });
        }
    }

    public static void BroadcastCanvasAction(string action)
    {
        lock (_broadcastLock)
        {
            _allBridges.RemoveAll(wr => !wr.TryGetTarget(out _));
            foreach (var wr in _allBridges)
                if (wr.TryGetTarget(out var b))
                    b.SendEvent("cze_remote_action", new { action });
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    // For getting window title text
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hWnd);

    public WebBridge(CoreWebView2 webView, System.Windows.Window window)
    {
        _webView = webView;
        _window  = window;
    }

    public void Initialize()
    {
        _webView.WebMessageReceived += OnWebMessageReceived;
        lock (_broadcastLock)
        {
            _allBridges.RemoveAll(wr => !wr.TryGetTarget(out _));
            _allBridges.Add(new WeakReference<WebBridge>(this));
        }

        // Wire up orchestrator progress events
        WorkspaceOrchestrator.Instance.ProgressChanged += (msg, pct) =>
            SendEvent("launch_progress", new { status = pct >= 100 ? "done" : "launching", message = msg, progress = pct });

        // Stream all system logs to the UI
        Logger.OnLog += (entry) => SendEvent("system_log", entry);
    }

    // ── Incoming from JS ───────────────────────────────────────────────────
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root      = doc.RootElement;
            string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(action))
                action = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            string? reqId = root.TryGetProperty("requestId", out var r) ? r.GetString() : null;

            Logger.Info($"[Bridge] Received action: {action} (requestId: {reqId ?? "none"})");

            // Extract payload (for actions that send nested payload)
            JsonElement payload = root.TryGetProperty("payload", out var p) ? p : root;

            switch (action)
            {
                // ── Fire-and-forget actions ──────────────────────────────
                case "get_state":
                    HandleGetState();
                    break;

                case "launch_workspace":
                    await HandleLaunchWorkspace(payload);
                    break;

                case "restore_workspace":
                    await HandleRestoreWorkspace(payload);
                    break;

                case "clean_workspace":
                    await HandleCleanWorkspace(payload);
                    break;

                case "save_item":
                    await HandleSaveItem(payload);
                    break;

                case "delete_item":
                    await HandleDeleteItem(payload);
                    break;

                case "move_item":
                    await HandleMoveItem(payload);
                    break;

                case "move_category":
                    await HandleMoveCategory(payload);
                    break;

                case "add_category":
                    await HandleAddCategory(payload);
                    break;

                case "delete_category":
                    await HandleDeleteCategory(payload);
                    break;

                case "rename_category":
                    await HandleRenameCategory(payload);
                    break;

                case "save_config":
                    await HandleSaveConfig(payload);
                    break;

                case "set_last_category":
                    HandleSetLastCategory(payload);
                    break;
                
                case "set_hotkeys_enabled":
                    HotkeyProcessor.Instance.Enabled = payload.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                    break;

                case "save_fz_path":
                    await HandleSaveFzPath(payload);
                    break;

                case "window_minimize":
                    _window.Dispatcher.Invoke(() => _window.WindowState = System.Windows.WindowState.Minimized);
                    break;

                case "window_maximize":
                    _window.Dispatcher.Invoke(() =>
                    {
                        _window.WindowState = _window.WindowState == System.Windows.WindowState.Maximized
                            ? System.Windows.WindowState.Normal
                            : System.Windows.WindowState.Maximized;
                    });
                    break;
                case "window_close":
                    _window.Dispatcher.Invoke(() => _window.Close());
                    break;
                
                case "window_drag":
                    Logger.Info("[WebBridge] Handling window drag request");
                    _window.Dispatcher.Invoke(() => {
                        try 
                        { 
                            User32.ReleaseCapture();
                            var handle = new WindowInteropHelper(_window).Handle;
                            User32.SendMessage(handle, 0xA1 /* WM_NCLBUTTONDOWN */, (nint)0x2 /* HT_CAPTION */, 0);
                        } 
                        catch (Exception ex) { Logger.Error($"[WebBridge] Drag failed: {ex.Message}"); }
                    });
                    break;

                // ── Request/Response (invoke) actions ────────────────────
                case "list_monitors":
                    ReplyInvoke(reqId, HandleListMonitors());
                    break;

                case "list_desktops":
                    ReplyInvoke(reqId, HandleListDesktops());
                    break;

                case "list_windows":
                    ReplyInvoke(reqId, HandleListWindows());
                    break;
                
                case "list_fancyzones":
                    _ = Task.Run(() => {
                        try {
                            var res = HandleListFancyZones();
                            ReplyInvoke(reqId, res);
                        } catch (Exception ex) {
                            Logger.Error($"[Bridge] list_fancyzones async error: {ex.Message}");
                            ReplyInvoke(reqId, new { error = ex.Message });
                        }
                    });
                    break;

                case "get_fz_status":
                    _ = Task.Run(() => {
                        try {
                            var status = HandleGetFzStatus();
                            ReplyInvoke(reqId, status);
                        } catch (Exception ex) {
                            Logger.Error($"[Bridge] get_fz_status async error: {ex.Message}");
                            ReplyInvoke(reqId, new { error = ex.Message });
                        }
                    });
                    break;

                case "change_layout_assignment":
                    ReplyInvoke(reqId, HandleChangeLayoutAssignment(payload));
                    break;

                case "get_config_path":
                    ReplyInvoke(reqId, HandleGetConfigPath());
                    break;

                case "open_config_folder":
                    HandleOpenConfigFolder();
                    break;

                case "change_config_path":
                    ReplyInvoke(reqId, HandleChangeConfigPath(payload));
                    break;

                case "get_windows_to_clean":
                    ReplyInvoke(reqId, await HandleGetWindowsToClean(payload));
                    break;

                case "close_windows":
                    ReplyInvoke(reqId, await HandleCloseWindows(payload));
                    break;

                case "validate_workspace":
                    ReplyInvoke(reqId, HandleValidateWorkspace(payload));
                    break;

                case "sync_workspace_layouts":
                    ReplyInvoke(reqId, HandleSyncWorkspaceLayouts(payload));
                    break;

                case "resolve_monitor_conflicts":
                    ReplyInvoke(reqId, await HandleResolveMonitorConflicts(payload));
                    break;

                case "open_file_dialog":
                    var dialogResult = await HandleOpenFileDialog(payload);
                    ReplyInvoke(reqId, dialogResult);
                    break;

                // ── CZE: fire-and-forget ──────────────────────────────
                case "cze_set_zone_engine":
                    HandleCzeSetZoneEngine(payload);
                    break;

                // ── CZE: invoke (request/response) ───────────────────
                case "cze_get_zone_engine":
                    ReplyInvoke(reqId, HandleCzeGetZoneEngine());
                    break;

                case "cze_get_layouts":
                    ReplyInvoke(reqId, HandleCzeGetLayouts());
                    break;

                case "cze_save_layout":
                    ReplyInvoke(reqId, HandleCzeSaveLayout(payload));
                    break;

                case "cze_delete_layout":
                    ReplyInvoke(reqId, HandleCzeDeleteLayout(payload));
                    break;

                case "cze_get_state":
                    ReplyInvoke(reqId, new { state = WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.State.ToString().ToLowerInvariant() });
                    break;

                case "cze_get_active_layouts":
                    ReplyInvoke(reqId, HandleCzeGetActiveLayouts());
                    break;

                case "cze_set_active_layout":
                    ReplyInvoke(reqId, HandleCzeSetActiveLayout(payload));
                    break;

                case "cze_open_canvas":
                    HandleCzeOpenCanvas(payload);
                    break;

                case "cze_canvas_saved":
                    WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: false);
                    break;
                case "cze_canvas_discard":
                    WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.ReturnToAdmin(isDiscard: true);
                    break;

                case "cze_request_save":
                     WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.RequestCanvasSave();
                     break;

                case "cze_request_discard":
                     WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.RequestCanvasDiscard();
                     break;

                case "cze_activate_manager":
                    WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.ActivateManager();
                    break;

                default:
                    Console.WriteLine($"[Bridge] Unknown action: {action}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bridge] Message error: {ex.Message}");
            SendEvent("error", new { message = ex.Message });
        }
    }

    // ── Invoke response helper ────────────────────────────────────────────
    private void ReplyInvoke(string? requestId, object? result)
    {
        if (requestId == null) return;
        SendEvent("invoke_response", new { requestId, result });
    }

    // ── Handlers: Fire-and-Forget ─────────────────────────────────────────

    private void HandleGetState()
    {
        var config = ConfigManager.Instance.Config;
        
        // Sync CategoryOrder with Apps keys
        var keys = config.Apps.Keys.ToList();
        
        // Remove ones that don't exist anymore
        config.CategoryOrder.RemoveAll(k => !config.Apps.ContainsKey(k));
        
        // Add new ones
        foreach (var k in keys)
        {
            if (!config.CategoryOrder.Contains(k))
                config.CategoryOrder.Add(k);
        }

        SendEvent("state_update", new
        {
            categories     = config.Apps,
            categoryOrder  = config.CategoryOrder,
            lastCategory   = config.LastCategory,
            hotkeys        = config.Hotkeys,
            pipWatcher     = config.PipWatcherEnabled,
            fzLayoutsCache = config.FzLayoutsCache,
            fzCustomPath   = config.FzCustomPath,
            fzDetectedPath = FancyZonesReader.FzBasePath,
            configPath     = ConfigManager.Instance.ConfigPath
        });
    }

    private async Task HandleLaunchWorkspace(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(category))
            category = ConfigManager.Instance.Config.LastCategory;
        if (string.IsNullOrEmpty(category)) return;

        ConfigManager.Instance.Config.LastCategory = category;
        await ConfigManager.Instance.SaveAsync();
        await WorkspaceOrchestrator.Instance.LaunchWorkspaceAsync(category);
    }

    private async Task HandleRestoreWorkspace(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(category))
            category = ConfigManager.Instance.Config.LastCategory;
        if (string.IsNullOrEmpty(category)) return;

        await WorkspaceOrchestrator.Instance.RestoreWorkspaceAsync(category);
    }

    private async Task HandleCleanWorkspace(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(category))
            category = ConfigManager.Instance.Config.LastCategory;
        if (string.IsNullOrEmpty(category)) return;

        await WorkspaceOrchestrator.Instance.CleanWorkspaceAsync(category);
    }

    private async Task HandleSaveItem(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    idx      = payload.TryGetProperty("index",    out var i) ? i.GetInt32() : -1;
        var    itemEl   = payload.TryGetProperty("payload",  out var p) ? p : default;

        if (string.IsNullOrEmpty(category)) return;

        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(category))
            config.Apps[category] = [];

        var item = JsonSerializer.Deserialize<AppItem>(itemEl.GetRawText(), JsonOpts);
        if (item == null) return;

        // Robustness: ensure we don't save decorated labels like "Monitor 1 ★" 
        // as the actual monitor path/ID if possible, but the Orchestrator 
        // already handles fuzzy matching, so we just trim.
        item.Monitor = item.Monitor?.Trim() ?? "Por defecto";
        item.Desktop = item.Desktop?.Trim() ?? "Por defecto";

        Logger.Info($"[WebBridge] Guardando item '{item.Path}' en escritorio '{item.Desktop}'");

        if (idx >= 0 && idx < config.Apps[category].Count)
            config.Apps[category][idx] = item;
        else
            config.Apps[category].Add(item);

        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleDeleteItem(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    idx      = payload.TryGetProperty("index",    out var i) ? i.GetInt32() : -1;

        var config = ConfigManager.Instance.Config;
        if (config.Apps.TryGetValue(category, out var items) && idx >= 0 && idx < items.Count)
        {
            items.RemoveAt(idx);
            await ConfigManager.Instance.SaveAsync();
        }
        HandleGetState();
    }

    private async Task HandleMoveItem(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    from     = payload.TryGetProperty("from",     out var f) ? f.GetInt32() : -1;
        int    to       = payload.TryGetProperty("to",       out var t) ? t.GetInt32() : -1;

        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items)) return;
        if (from < 0 || to < 0 || from >= items.Count || to >= items.Count) return;

        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);

        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleMoveCategory(JsonElement payload)
    {
        int from = payload.TryGetProperty("from", out var f) ? f.GetInt32() : -1;
        int to   = payload.TryGetProperty("to",   out var t) ? t.GetInt32() : -1;

        var config = ConfigManager.Instance.Config;
        if (from < 0 || to < 0 || from >= config.CategoryOrder.Count || to >= config.CategoryOrder.Count) return;

        var cat = config.CategoryOrder[from];
        config.CategoryOrder.RemoveAt(from);
        config.CategoryOrder.Insert(to, cat);

        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleAddCategory(JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name)) return;
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(name))
        {
            config.Apps[name] = [];
            config.CategoryOrder.Add(name);
        }
        config.LastCategory = name;
        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleDeleteCategory(JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var config = ConfigManager.Instance.Config;
        config.Apps.Remove(name);
        config.CategoryOrder.Remove(name);
        if (config.LastCategory == name)
            config.LastCategory = config.CategoryOrder.FirstOrDefault() ?? "";
        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleRenameCategory(JsonElement payload)
    {
        string oldName = payload.TryGetProperty("oldName", out var o) ? o.GetString() ?? "" : "";
        string newName = payload.TryGetProperty("newName", out var n) ? n.GetString() ?? "" : "";
        
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
        
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(oldName)) return;

        // 1. Swap dictionary keys
        var items = config.Apps[oldName];
        config.Apps.Remove(oldName);
        config.Apps[newName] = items;

        // 2. Update order list
        int idx = config.CategoryOrder.IndexOf(oldName);
        if (idx >= 0) config.CategoryOrder[idx] = newName;

        // 3. Update last category if needed
        if (config.LastCategory == oldName) config.LastCategory = newName;

        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleSaveConfig(JsonElement payload)
    {
        var config = ConfigManager.Instance.Config;
        if (payload.TryGetProperty("hotkeys", out var hk))
            config.Hotkeys = JsonSerializer.Deserialize<HotkeyConfig>(hk.GetRawText(), JsonOpts) ?? config.Hotkeys;
        if (payload.TryGetProperty("pipWatcherEnabled", out var pip))
            config.PipWatcherEnabled = pip.GetBoolean();
        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleSaveFzPath(JsonElement payload)
    {
        string path = payload.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        ConfigManager.Instance.Config.FzCustomPath = string.IsNullOrWhiteSpace(path) ? null : path;
        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private void HandleSetLastCategory(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        ConfigManager.Instance.Config.LastCategory = category;
        ConfigManager.Instance.Save();
    }

    // ── Handlers: Request/Response (invoke) ───────────────────────────────

    private object HandleListMonitors()
    {
        var monitors = MonitorManager.GetActiveMonitors();
        return monitors.Select(m => new
        {
            id            = m.Handle,
            deviceName    = m.DeviceName,
            hardwareId    = m.HardwareId,
            ptName        = m.PtName,
            ptInstance    = m.PtInstance,
            name          = m.Name,
            label         = $"{m.Name}{(m.IsPrimary ? " ★" : "")}",
            isPrimary     = m.IsPrimary,
            monitorNumber = m.MonitorNumber,
            scale         = m.Scale,
            bounds        = new { width = m.Bounds.Width,   height = m.Bounds.Height },
            workArea      = new { width = m.WorkArea.Width, height = m.WorkArea.Height,
                                  left  = m.WorkArea.Left,  top    = m.WorkArea.Top },
        }).ToArray();
    }

    private object HandleListDesktops()
    {
        try
        {
            var vdm = VirtualDesktopManager.Instance;
            var desktopList = vdm.GetDesktops();
            var activeDesktopId = vdm.GetCurrentDesktopId();
            
            if (desktopList.Count == 0)
            {
                return new[] { new { id = Guid.Empty.ToString(), name = "Escritorio 1", isCurrent = true } };
            }

            return desktopList.Select((id, idx) => new
            {
                id = id.ToString().ToLowerInvariant(),
                name = $"Escritorio {idx + 1}",
                isCurrent = activeDesktopId.HasValue && activeDesktopId.Value == id
            }).ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleListDesktops error: {ex.Message}");
            return new[] { new { id = Guid.Empty.ToString(), name = "Escritorio 1", isCurrent = true } };
        }
    }

    private object HandleListFancyZones()
    {
        try
        {
            var layouts = FancyZonesReader.GetAvailableLayouts();
            return layouts.Select(l => new {
                uuid = l.uuid,
                name = l.name,
                type = l.type,
                isCustom = l.isCustom,
                zoneCount = l.zoneCount
                // info excluded to keep payload small
            }).ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleListFancyZones error: {ex.Message}");
            return new List<object>();
        }
    }

    private object HandleGetFzStatus()
    {
        try
        {
            Logger.Info("[WebBridge] HandleGetFzStatus: Starting status collection...");
            var applied = FancyZonesReader.ReadAppliedLayouts();
            Logger.Info($"[WebBridge] HandleGetFzStatus: Read {applied.Count} applied layouts.");
            
            var monitors = MonitorManager.GetActiveMonitors();
            Logger.Info($"[WebBridge] HandleGetFzStatus: Found {monitors.Count} active monitors.");

            var vdm = VirtualDesktopManager.Instance;
            var desktopIds = vdm.GetDesktops();
            var activeDesktop = vdm.GetCurrentDesktopId();
            Logger.Info($"[WebBridge] HandleGetFzStatus: Found {desktopIds.Count} virtual desktops.");

            var desktops = desktopIds.Count > 0
                ? desktopIds.Select((id, idx) => new
                {
                    id = id.ToString().ToLowerInvariant(),
                    name = $"Escritorio {idx + 1}",
                    isCurrent = activeDesktop.HasValue && activeDesktop.Value == id
                }).ToArray()
                : new[] { new { id = Guid.Empty.ToString(), name = "Escritorio 1", isCurrent = true } };

            var availableLayouts = FancyZonesReader.GetAvailableLayouts();
            Logger.Info($"[WebBridge] HandleGetFzStatus: Found {availableLayouts.Count} available layouts.");

            // Build a map: for each monitor+desktop, find the active layout
            var statusEntries = new List<object>();
            foreach (var mon in monitors)
            {
                Logger.Info($"[WebBridge] Processing status for monitor: {mon.Name} ({mon.PtInstance})");
                foreach (var dk in desktops)
                {
                    try
                    {
                        Logger.Info($"[FzStatus] Checking Monitor {mon.Name} ({mon.PtName}) for Desktop {dk.name} ({dk.id})");
                        var matchedLayout = (object?)null;
                        string? matchedLayoutUuid = null;
                        bool isAllDesktopsMatched = false;
                        int bestMatchQuality = -1;

                        string monInstNorm = (mon.PtInstance ?? "").Trim('{', '}').ToLowerInvariant();
                        string monPtNorm = (mon.PtName ?? "").ToLowerInvariant();
                        string monSerial = (mon.SerialNumber ?? "").ToLowerInvariant();

                        foreach (var ae in applied)
                        {
                            string aeInstNorm = (ae.Instance ?? "").Trim('{', '}').ToLowerInvariant();
                            string aeMonNorm = (ae.MonitorName ?? "").ToLowerInvariant();
                            string aeSerial = (ae.SerialNumber ?? "").ToLowerInvariant();

                            // ── Monitor matching (High resolution) ──
                            bool serialMatch = !string.IsNullOrEmpty(monSerial) && monSerial == aeSerial;
                            bool instMatch = !string.IsNullOrEmpty(aeInstNorm) && aeInstNorm == monInstNorm;
                            bool nameMatch = !string.IsNullOrEmpty(aeMonNorm) && (aeMonNorm == monPtNorm || aeMonNorm.Contains(monPtNorm));

                            if (!serialMatch && !instMatch && !nameMatch) continue;

                            // ── Desktop matching (Strict) ──
                            string dkIdNorm = dk.id.Trim('{', '}').ToLowerInvariant();
                            string aeDkNorm = (ae.DesktopId ?? "").Trim('{', '}').ToLowerInvariant();

                            // A match is either the exact desktop GUID, or the "all desktops" GUID
                            bool isAllDesktops = string.IsNullOrEmpty(aeDkNorm) || aeDkNorm == "00000000-0000-0000-0000-000000000000";
                            bool isExactMatch = aeDkNorm == dkIdNorm;

                            if (isExactMatch || isAllDesktops)
                            {
                                string rawUuid = ae.LayoutUuid.Trim('{', '}').ToLowerInvariant();
                                var layout = TryMatchLayout(rawUuid, ae.LayoutType, availableLayouts);

                                // Priority: Prefer Serial matches > Instance matches > Name matches.
                                // Prefer Exact Desktop > All Desktops.
                                // Higher quality match wins; same quality → last entry in file wins.
                                int matchQuality = (serialMatch ? 100 : 0) + (instMatch ? 50 : 0) + (nameMatch ? 10 : 0);
                                bool isHigherPriority = (isExactMatch && isAllDesktopsMatched);
                                bool isSamePriority = (isExactMatch == !isAllDesktopsMatched);

                                // Use match quality as tiebreaker: prevents ghost entries (e.g. a disconnected
                                // monitor that shares the same PnP instance) from overriding the real one.
                                bool shouldUpdate = (matchedLayout == null)
                                    || isHigherPriority
                                    || (isSamePriority && matchQuality >= bestMatchQuality);

                                if (shouldUpdate)
                                {
                                    Logger.Info($"[FzStatus] Found match for {mon.Name}: {ae.LayoutType} (UUID: {rawUuid}). Quality={matchQuality}, Type={(isExactMatch ? "Exact" : "Global")}");
                                    isAllDesktopsMatched = isAllDesktops;
                                    bestMatchQuality = matchQuality;
                                    if (layout != null)
                                    {
                                        matchedLayoutUuid = layout.uuid;
                                        matchedLayout = new { 
                                            uuid = layout.uuid, 
                                            name = layout.name,
                                            isCustom = layout.isCustom,
                                            type = layout.type
                                        };
                                    }
                                    else
                                    {
                                        string displayName = ae.LayoutType;
                                        // Standardize display names
                                        if (displayName.Equals("grid", StringComparison.OrdinalIgnoreCase)) displayName = "Cuadrícula";
                                        else if (displayName.Equals("priority-grid", StringComparison.OrdinalIgnoreCase)) displayName = "Cuadrícula de prioridad";
                                        else if (displayName.Equals("rows", StringComparison.OrdinalIgnoreCase)) displayName = "Filas";
                                        else if (displayName.Equals("columns", StringComparison.OrdinalIgnoreCase)) displayName = "Columnas";
                                        else if (displayName.Equals("focus", StringComparison.OrdinalIgnoreCase)) displayName = "Foco";
                                        else if (displayName.Equals("blank", StringComparison.OrdinalIgnoreCase)) displayName = "Sin diseño";
                                        
                                        matchedLayout = new { 
                                            uuid = rawUuid, 
                                            name = displayName,
                                            isCustom = (ae.LayoutType == "custom"),
                                            type = ae.LayoutType
                                        };
                                    }
                                }
                                
                                // If we found an exact match, we can stop searching for this specific monitor+desktop
                                // DO NOT break here. We want to find the LAST exact match in the file if multiple exist.
                            }
                        }

                        statusEntries.Add(new
                        {
                            monitorId = mon.Handle,
                            monitorLabel = mon.Name,
                            monitorPtName = mon.PtName,
                            monitorSerial = mon.SerialNumber,
                            monitorPtInstance = mon.PtInstance,
                            monitorHardwareId = mon.HardwareId,
                            desktopId = dk.id,
                            desktopName = dk.name,
                            desktopIsCurrent = dk.isCurrent,
                            activeLayout = matchedLayout,
                            activeLayoutUuid = matchedLayoutUuid
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[WebBridge] Error processing entry for {mon.Name}/{dk.name}: {ex.Message}");
                    }
                }
            }

            Logger.Info($"[WebBridge] Status collection complete. Sending {statusEntries.Count} entries.");
            return new
            {
                entries = statusEntries,
                layouts = availableLayouts.Select(l => new {
                    uuid = l.uuid,
                    name = l.name,
                    type = l.type,
                    isCustom = l.isCustom,
                    zoneCount = l.zoneCount
                }).ToList(),
                monitors = monitors.Select(m => new
                {
                    id = m.Handle,
                    deviceName = m.DeviceName,
                    hardwareId = m.HardwareId,
                    ptName = m.PtName,
                    ptInstance = m.PtInstance,
                    name = m.Name,
                    // Identity is just the name, label is for display only
                    label = m.Name,
                    displayLabel = $"{m.Name}{(m.IsPrimary ? " ★" : "")}",
                    isPrimary = m.IsPrimary,
                }).ToArray(),
                desktops
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleGetFzStatus CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
            SendEvent("error", new { message = "Error al leer FancyZones: " + ex.Message });
            return new { 
                entries = new List<object>(), 
                layouts = new List<object>(), 
                monitors = new List<object>(), 
                desktops = new List<object>(),
                error = ex.Message 
            };
        }
    }

    private FzLayoutInfo? TryMatchLayout(string uuid, string type, List<FzLayoutInfo> available)
    {
        string cleanUuid = uuid.Trim('{', '}').ToLowerInvariant();
        
        // 1. Try exact UUID match
        var match = available.FirstOrDefault(l => l.uuid.Equals(cleanUuid, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 2. Try matching by type if the UUID is the zeros-GUID (standard PowerToys behavior for templates)
        if (cleanUuid == "00000000-0000-0000-0000-000000000000" && type != "custom")
        {
            return available.FirstOrDefault(l => !l.isCustom && l.type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Last chance: if it's a known template type but PowerToys assigned a specific GUID 
        // that we didn't happen to read (or it's one of our shorthand ones like 'grid'), check by type
        if (type != "custom")
        {
            return available.FirstOrDefault(l => !l.isCustom && l.type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private object HandleChangeLayoutAssignment(JsonElement payload)
    {
        try
        {
            string monitorInstance = payload.TryGetProperty("monitorInstance", out var mi) ? mi.GetString() ?? "" : "";
            string monitorName = payload.TryGetProperty("monitorName", out var mn) ? mn.GetString() ?? "" : "";
            string? desktopId = payload.TryGetProperty("desktopId", out var di) ? di.GetString() : null;
            string monitorSerial = payload.TryGetProperty("monitorSerial", out var ms) ? ms.GetString() ?? "" : "";
            string layoutUuid = payload.TryGetProperty("layoutUuid", out var lu) ? lu.GetString() ?? "" : "";
            string layoutType = payload.TryGetProperty("layoutType", out var lt) ? lt.GetString() ?? "custom" : "custom";

            bool ok = false;
            if (!string.IsNullOrEmpty(layoutUuid))
            {
                var config = ConfigManager.Instance.Config;
                string cleanUuid = layoutUuid.Trim('{', '}').ToLowerInvariant();
                if (config.FzLayoutsCache.TryGetValue(cleanUuid, out var cached))
                {
                    layoutType = cached.Type;
                }
                ok = FancyZonesReader.InjectLayoutByDevice(monitorInstance, monitorName, monitorSerial, desktopId, cleanUuid, layoutType);
            }
            else
            {
                // Assign empty/none layout
                ok = FancyZonesReader.InjectLayoutByDevice(monitorInstance, monitorName, monitorSerial, desktopId, "", "blank");
            }

            if (ok)
            {
                FancyZonesReader.SyncCacheFromDisk();
                Logger.Info($"[WebBridge] Layout changed: {monitorName} desktop={desktopId} → {layoutUuid}");
            }
            return new { success = ok };
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleChangeLayoutAssignment error: {ex.Message}");
            return new { success = false, error = ex.Message };
        }
    }

    private object HandleListWindows()
    {
        var windows = WindowManager.GetVisibleWindows();
        var result  = new List<object>();

        foreach (var hwnd in windows)
        {
            int len = GetWindowTextLengthW(hwnd);
            if (len <= 0) continue;

            var buf = new char[len + 1];
            GetWindowTextW(hwnd, buf, buf.Length);
            string title = new(buf, 0, len);

            if (string.IsNullOrWhiteSpace(title)) continue;

            // Get process name
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "";
            try
            {
                var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { }

            result.Add(new { hwnd = (long)hwnd, title, processName });
        }

        return result;
    }

    private Task<object> HandleGetWindowsToClean(JsonElement payload)
    {
        try
        {
            string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(category)) return Task.FromResult<object>(new List<object>());

            var config = ConfigManager.Instance.Config;
            if (!config.Apps.TryGetValue(category, out var items)) return Task.FromResult<object>(new List<object>());

            var allWindows = WindowManager.GetVisibleWindows();
            var detectedHwnds = new HashSet<nint>();
            var result = new List<object>();

            foreach (var item in items)
            {
                // Find ALL windows that match this item with a decent score
                foreach (var hwnd in allWindows)
                {
                    if (detectedHwnds.Contains(hwnd)) continue;

                    // Use the refined public scoring logic
                    int score = WindowDetector.CalculateScore(item, hwnd);
                    if (score >= 3)
                    {
                        detectedHwnds.Add(hwnd);
                        User32.GetWindowThreadProcessId(hwnd, out uint pid);
                        string processName = "";
                        try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { }

                        string appRef = item.Type == "exe" && !string.IsNullOrEmpty(item.Path)
                            ? Path.GetFileNameWithoutExtension(item.Path) 
                            : item.Path ?? "App";

                        result.Add(new { 
                            hwnd = (long)hwnd, 
                            title = WindowManager.GetWindowTitle(hwnd),
                            processName = processName,
                            appName = appRef
                        });
                    }
                }
            }
            return Task.FromResult<object>(result);
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleGetWindowsToClean error: {ex.Message}");
            return Task.FromResult<object>(new List<object>());
        }
    }

    private Task<object> HandleCloseWindows(JsonElement payload)
    {
        try 
        {
            if (payload.TryGetProperty("hwnds", out var hwnds) && hwnds.ValueKind == JsonValueKind.Array)
            {
                foreach (var hEl in hwnds.EnumerateArray())
                {
                    nint hwnd = (nint)hEl.GetInt64();
                    User32.SendMessage(hwnd, User32.WM_CLOSE, 0, 0);
                    ZoneStack.Instance.Unregister(hwnd);
                }
            }
            return Task.FromResult<object>(new { success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new { success = false, message = ex.Message });
        }
    }

    private object HandleValidateWorkspace(JsonElement payload)
    {
        string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(category)) return new { valid = true };

        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items)) return new { valid = true };

        // ── Auto-fix and repair on the fly ──
        WorkspaceResolver.ResolveEnvironment(items);

        FancyZonesReader.SyncCacheFromDisk();
        var currentLayouts = FancyZonesReader.ReadCustomLayouts();
        var currentMonitors = MonitorManager.GetActiveMonitors();
        var currentDesktopId = VirtualDesktopManager.Instance.GetCurrentDesktopId()?.ToString().ToLowerInvariant() ?? "";
        var appliedLayouts = FancyZonesReader.ReadAppliedLayouts();
        var layouts = FancyZonesReader.GetAvailableLayouts();
        
        var warnings = new List<object>();
        var missingLayouts = new List<string>(); // UUIDs

        // Find how many desktops we need
        int maxDesktopNeeded = 1;
        foreach(var itm in items) {
           if (WorkspaceOrchestrator.TryParseDesktopIndex(itm.Desktop, out int dIdx)) {
               if (dIdx > maxDesktopNeeded) maxDesktopNeeded = dIdx;
           }
        }
        var activeDesktopsCount = VirtualDesktopManager.Instance.GetDesktops().Count;
        if (maxDesktopNeeded > activeDesktopsCount)
        {
            warnings.Add(new {
                type = "desktop_missing",
                desktopsNeeded = maxDesktopNeeded - activeDesktopsCount,
                message = $"El workspace requiere {maxDesktopNeeded} escritorios, pero solo hay {activeDesktopsCount}. Se crearán automáticamente."
            });
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            string cleanUuid = item.FancyzoneUuid?.Trim('{', '}').ToLowerInvariant() ?? "";

            // 1. Check if Layout is MISSING (not in PowerToys)
            if (!string.IsNullOrEmpty(cleanUuid))
            {
                // Standard templates (grid, rows, columns, priority-grid, focus) are built-in and never "missing"
                bool isStandardTemplate = new[] { "grid", "rows", "columns", "priority-grid", "focus" }.Contains(cleanUuid, StringComparer.OrdinalIgnoreCase);

                if (!isStandardTemplate && !currentLayouts.ContainsKey(cleanUuid))
                {
                    // Check if we have it in cache
                    if (config.FzLayoutsCache.TryGetValue(cleanUuid, out var cached))
                    {
                        warnings.Add(new { 
                            type = "layout_missing", 
                            itemPath = item.Path,
                            layoutName = cached.Name,
                            layoutUuid = cleanUuid,
                            info = cached.Info, // Added
                            message = $"El layout '{cached.Name}' no está en PowerToys de este equipo."
                        });
                        if (!missingLayouts.Contains(cleanUuid)) missingLayouts.Add(cleanUuid);
                    }
                    else
                    {
                        warnings.Add(new { 
                            type = "layout_lost", 
                            itemPath = item.Path,
                            layoutUuid = cleanUuid,
                            message = $"El layout (UUID: {cleanUuid}) no se encuentra ni en el equipo ni en el caché."
                        });
                    }
                }
            }

            // 2. Check Monitor
            bool isDefaultOrEmpty = string.IsNullOrEmpty(item.Monitor) || item.Monitor == "Por defecto";
            bool monitorExists = isDefaultOrEmpty ? false : WorkspaceResolver.IsMonitorAvailable(item.Monitor, currentMonitors);
            
            if (!monitorExists)
            {
                string proposed = WorkspaceResolver.MapMissingMonitor(isDefaultOrEmpty ? "Pantalla 1" : item.Monitor, currentMonitors);

                warnings.Add(new { 
                    type = "monitor_missing", 
                    itemPath = item.Path,
                    itemIndex = i,
                    monitorName = isDefaultOrEmpty ? "Sin Asignar (Por defecto)" : item.Monitor,
                    proposedMonitor = proposed,
                    message = isDefaultOrEmpty ? "La aplicación no tiene monitor asignado. ¿Dónde quieres que se abra?" : $"El monitor '{item.Monitor}' no está conectado. ¿A qué pantalla deseas asignarlo?"
                });
            }

            // 3. Check if Layout is DIFFERENT from active (Mismatch)
            if (monitorExists && !string.IsNullOrEmpty(cleanUuid))
            {
                // Resolve which desktop we should check for this specific app
                var itemDesktopGuid = WorkspaceOrchestrator.ResolveDesktopGuid(item);
                string targetDesktopId = itemDesktopGuid?.ToString().ToLowerInvariant() ?? currentDesktopId;

                // Find what's currently active on that specific monitor ON THE TARGET DESKTOP
                var mon = currentMonitors.FirstOrDefault(m => 
                    m.PtName == item.Monitor || 
                    m.Name.Contains(item.Monitor, StringComparison.OrdinalIgnoreCase) ||
                    m.HardwareId.Contains(item.Monitor, StringComparison.OrdinalIgnoreCase));

                if (mon != null)
                {
                    // Find what's currently active on that specific monitor ON THE TARGET DESKTOP
                    string monSerial = (mon.SerialNumber ?? "").ToLowerInvariant();
                    string monInstNorm = (mon.PtInstance ?? "").Trim('{', '}').ToLowerInvariant();
                    string monPtNorm = (mon.PtName ?? "").ToLowerInvariant();

                    var active = (AppliedLayoutEntry?)null;
                    bool foundExact = false;
                    foreach (var a in appliedLayouts)
                    {
                        string aeInstNorm = (a.Instance ?? "").Trim('{', '}').ToLowerInvariant();
                        string aeMonNorm = (a.MonitorName ?? "").ToLowerInvariant();
                        string aeDkNorm = (a.DesktopId ?? "").Trim('{', '}').ToLowerInvariant();
                        string aeSerial = (a.SerialNumber ?? "").ToLowerInvariant();
                        
                        bool serialMatch = !string.IsNullOrEmpty(monSerial) && monSerial == aeSerial;
                        bool instMatch = !string.IsNullOrEmpty(aeInstNorm) && aeInstNorm == monInstNorm;
                        bool nameMatch = !string.IsNullOrEmpty(aeMonNorm) && (aeMonNorm == monPtNorm || aeMonNorm.Contains(monPtNorm));

                        if (!serialMatch && !instMatch && !nameMatch) continue;

                        bool isExact = aeDkNorm == targetDesktopId.Trim('{', '}').ToLowerInvariant();
                        bool isAll = aeDkNorm == "00000000-0000-0000-0000-000000000000" || string.IsNullOrEmpty(aeDkNorm);

                        if (isExact)
                        {
                            active = a;
                            foundExact = true;
                            // DO NOT break. We want to take the LAST match in the file.
                        }
                        else if (isAll && !foundExact)
                        {
                            active = a;
                        }
                    }

                    if (active != null)
                    {
                        string activeUuid = active.LayoutUuid.Trim('{', '}').ToLowerInvariant();
                        if (activeUuid != cleanUuid)
                        {
                            var assignedLayout = layouts.FirstOrDefault(l => l.uuid.Equals(cleanUuid, StringComparison.OrdinalIgnoreCase));
                            
                            // Use robust matching
                            var activeLayout = TryMatchLayout(activeUuid, active.LayoutType, layouts);

                            // Logical match check: if the resolved activeLayout has the exact same UUID as assigned, it's NOT a mismatch
                            if (activeLayout != null && assignedLayout != null && 
                                (activeLayout.uuid.Equals(assignedLayout.uuid, StringComparison.OrdinalIgnoreCase) ||
                                (!activeLayout.isCustom && !assignedLayout.isCustom && activeLayout.type.Equals(assignedLayout.type, StringComparison.OrdinalIgnoreCase))))
                            {
                                // They are functionally the same layout, skip mismatch warning
                            }
                            else
                            {
                                string assignedName = assignedLayout?.name ?? "Configurado";
                                string currentName = activeLayout?.name ?? active.LayoutType;
                                
                                // Last fallback for internal types not found in lists
                                if (currentName.Equals("blank", StringComparison.OrdinalIgnoreCase)) currentName = "Sin diseño";
                                else if (currentName.Equals("custom", StringComparison.OrdinalIgnoreCase)) currentName = "Personalizado";
                                 
                                string assignedType = assignedLayout?.type ?? "custom";

                                warnings.Add(new { 
                                    type = "layout_mismatch", 
                                    itemPath = item.Path,
                                    monitorName = item.Monitor,
                                    monitorInstance = mon.PtInstance,
                                    monitorSerial = mon.SerialNumber,
                                    desktopId = targetDesktopId,
                                    layoutUuid = cleanUuid,
                                    layoutType = assignedType,
                                    assignedLayout = assignedName,
                                    assignedInfo = assignedLayout?.info, // Added
                                    activeLayout = currentName,
                                    activeInfo = activeLayout?.info,   // Added
                                    message = $"Layout en '{item.Monitor}' ({item.Desktop}) es '{currentName}', pero el workspace requiere '{assignedName}'."
                                });
                            }
                        }
                    }
                    else 
                    {
                        // Portability fix: If no layout is applied on that desktop yet, we should also offer to set it
                        var assignedLayout = layouts.FirstOrDefault(l => l.uuid.Equals(cleanUuid, StringComparison.OrdinalIgnoreCase));
                        string assignedName = assignedLayout?.name ?? "Configurado";
                        string assignedType = assignedLayout?.type ?? "custom";

                        warnings.Add(new { 
                            type = "layout_mismatch", 
                            itemPath = item.Path,
                            monitorName = item.Monitor,
                            monitorInstance = mon.PtInstance,
                            monitorSerial = mon.SerialNumber,
                            desktopId = targetDesktopId,
                            layoutUuid = cleanUuid,
                            layoutType = assignedType,
                            assignedLayout = assignedName,
                            assignedInfo = assignedLayout?.info, // Added
                            activeLayout = "Ninguno",
                            activeInfo = (object?)null, // No active layout, so no info
                            message = $"No hay layout activo en '{item.Monitor}' ({item.Desktop}). El workspace requiere '{assignedName}'."
                        });
                    }
                }
            }
        }

        return new { 
            valid = warnings.Count == 0, 
            warnings,
            missingLayouts,
            activeMonitors = currentMonitors.Select(m => m.Name).ToArray(),
            availableLayouts = layouts.Select(l => new {
                uuid = l.uuid,
                name = l.name,
                type = l.type,
                isCustom = l.isCustom,
                info = l.info // Added
            }).ToArray()
        };
    }

    private object HandleSyncWorkspaceLayouts(JsonElement payload)
    {
        try
        {
            var config = ConfigManager.Instance.Config;
            if (payload.TryGetProperty("layoutUuids", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                int synced = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    string uuid = el.GetString() ?? "";
                    if (config.FzLayoutsCache.TryGetValue(uuid, out var layout))
                    {
                        bool ok = FancyZonesReader.UpsertCustomLayout(layout.Uuid, layout.Name, layout.Type, layout.Info);
                        if (ok) synced++;
                    }
                }
                return new { success = true, synced };
            }
            return new { success = false, message = "No UUIDs provided" };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }

    private async Task<object> HandleResolveMonitorConflicts(JsonElement payload)
    {
        try
        {
            string category = payload.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(category)) return new { success = false, message = "No category" };

            var config = ConfigManager.Instance.Config;
            if (!config.Apps.TryGetValue(category, out var items)) return new { success = false, message = "Category not found" };

            if (payload.TryGetProperty("resolutions", out var res) && res.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in res.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int idx) && idx >= 0 && idx < items.Count)
                    {
                        items[idx].Monitor = prop.Value.GetString() ?? "Por defecto";
                    }
                }
                await ConfigManager.Instance.SaveAsync();
                HandleGetState();
                return new { success = true };
            }
            return new { success = false, message = "Invalid resolutions object" };
        }
        catch (Exception ex)
        {
            return new { success = false, message = ex.Message };
        }
    }

    private object HandleGetConfigPath()
    {
        return new { path = ConfigManager.Instance.ConfigPath };
    }

    private void HandleOpenConfigFolder()
    {
        try
        {
            string path = ConfigManager.Instance.ConfigPath;
            string dir = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
            Process.Start("explorer.exe", dir);
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleOpenConfigFolder error: {ex.Message}");
        }
    }

    private object HandleChangeConfigPath(JsonElement payload)
    {
        try
        {
            string newPath = payload.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(newPath)) return new { success = false, error = "Ruta vacía" };

            // Ensure it has the correct filename
            if (!newPath.EndsWith("mis_apps_config_v2.json", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(newPath))
                {
                    newPath = Path.Combine(newPath, "mis_apps_config_v2.json");
                }
            }

            ConfigManager.Instance.ChangeConfigPath(newPath);
            HandleGetState(); // Push new state to UI
            return new { success = true };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<object?> HandleOpenFileDialog(JsonElement payload)
    {
        bool isFolder = payload.TryGetProperty("isFolder", out var f) && f.GetBoolean();

        // Must run on UI thread
        return await _window.Dispatcher.InvokeAsync(() =>
        {
            if (isFolder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = payload.TryGetProperty("title", out var t) ? t.GetString() ?? "Seleccionar carpeta" : "Seleccionar carpeta"
                };
                return dialog.ShowDialog(_window) == true ? (object?)dialog.FolderName : null;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Seleccionar archivo",
                    Filter = "Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*"
                };

                // Apply custom filters if provided
                if (payload.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
                {
                    var filterParts = new List<string>();
                    foreach (var flt in filters.EnumerateArray())
                    {
                        string name = flt.TryGetProperty("name", out var fn) ? fn.GetString() ?? "Archivos" : "Archivos";
                        var exts = flt.TryGetProperty("extensions", out var fe)
                            ? fe.EnumerateArray().Select(x => $"*.{x.GetString()}").ToArray()
                            : ["*.*"];
                        filterParts.Add($"{name} ({string.Join(";", exts)})|{string.Join(";", exts)}");
                    }
                    dialog.Filter = string.Join("|", filterParts);
                }

                return dialog.ShowDialog(_window) == true ? (object?)dialog.FileName : null;
            }
        });
    }

    // ── Handlers: Custom Zone Engine (CZE) ──────────────────────────────────

    private void HandleCzeSetZoneEngine(JsonElement payload)
    {
        string engine = payload.TryGetProperty("engine", out var e) ? e.GetString() ?? "fancyzones" : "fancyzones";
        ConfigManager.Instance.Config.ZoneEngine = engine;
        ConfigManager.Instance.Save();
        HandleGetState();
    }

    private object HandleCzeGetZoneEngine()
        => new { engine = ConfigManager.Instance.Config.ZoneEngine ?? "fancyzones" };

    private object HandleCzeGetLayouts()
    {
        var layouts = ConfigManager.Instance.Config.CzeLayouts.Values
            .Select(l => new {
                id        = l.Id,
                name      = l.Name,
                zones     = l.Zones,
                spacing   = l.Spacing,
                gridState = l.GridState,
                refWidth  = l.RefWidth,
                refHeight = l.RefHeight,
            })
            .ToList();
        return new { layouts };
    }

    private object HandleCzeSaveLayout(JsonElement payload)
    {
        try
        {
            var config   = ConfigManager.Instance.Config;
            var layoutEl = payload.TryGetProperty("layout", out var l) ? l : payload;

            string id   = layoutEl.TryGetProperty("id",   out var idEl)   ? idEl.GetString()   ?? Guid.NewGuid().ToString("D") : Guid.NewGuid().ToString("D");
            string name = layoutEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "New Layout"                 : "New Layout";

            var zones = new List<WorkspaceLauncher.Core.Config.CzeZoneEntry>();
            if (layoutEl.TryGetProperty("zones", out var zonesEl) && zonesEl.ValueKind == JsonValueKind.Array)
            {
                int autoId = 0;
                foreach (var z in zonesEl.EnumerateArray())
                {
                    zones.Add(new WorkspaceLauncher.Core.Config.CzeZoneEntry
                    {
                        Id = z.TryGetProperty("id", out var zid) ? zid.GetInt32() : autoId,
                        X  = z.TryGetProperty("x",  out var zx)  ? zx.GetInt32()  : 0,
                        Y  = z.TryGetProperty("y",  out var zy)  ? zy.GetInt32()  : 0,
                        W  = z.TryGetProperty("w",  out var zw)  ? zw.GetInt32()  : 10000,
                        H  = z.TryGetProperty("h",  out var zh)  ? zh.GetInt32()  : 10000,
                    });
                    autoId++;
                }
            }

            int spacing   = layoutEl.TryGetProperty("spacing",   out var spEl)  ? spEl.GetInt32()  : 0;
            int refWidth  = layoutEl.TryGetProperty("refWidth",  out var rwEl)  ? rwEl.GetInt32()  : 0;
            int refHeight = layoutEl.TryGetProperty("refHeight", out var rhEl)  ? rhEl.GetInt32()  : 0;
            string? gridState = layoutEl.TryGetProperty("gridState", out var gsEl) && gsEl.ValueKind == JsonValueKind.String
                ? gsEl.GetString() : null;

            var entry = new WorkspaceLauncher.Core.Config.CzeLayoutEntry
            {
                Id        = id,
                Name      = name,
                Zones     = zones,
                Spacing   = spacing,
                GridState = gridState,
                RefWidth  = refWidth,
                RefHeight = refHeight,
            };
            config.CzeLayouts[id] = entry;
            ConfigManager.Instance.Save();

            return new { ok = true, id, layout = entry };
        }
        catch (Exception ex)
        {
            Logger.Error($"[CZE] SaveLayout error: {ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    private object HandleCzeDeleteLayout(JsonElement payload)
    {
        try
        {
            string id = payload.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(id)) return new { ok = false, error = "id required" };

            var config = ConfigManager.Instance.Config;
            config.CzeLayouts.Remove(id);
            // Also remove any active layout mappings pointing to this layout
            var keysToRemove = config.CzeActiveLayouts.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
            foreach (var k in keysToRemove) config.CzeActiveLayouts.Remove(k);
            ConfigManager.Instance.Save();
            return new { ok = true };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private object HandleCzeGetActiveLayouts()
    {
        var config = ConfigManager.Instance.Config;
        var desktops = VirtualDesktopManager.Instance.GetDesktops();
        var monitors = MonitorManager.GetActiveMonitors();

        var entries = new List<object>();
        for (int m = 0; m < monitors.Count; m++)
        {
            var monitor = monitors[m];
            for (int d = 0; d < desktops.Count; d++)
            {
                var desktopId = desktops[d];
                string key = WorkspaceLauncher.Core.CustomZoneEngine.Models.ActiveLayoutMap.MakeKey(monitor.PtInstance, desktopId);
                config.CzeActiveLayouts.TryGetValue(key, out string? layoutId);
                entries.Add(new
                {
                    monitorPtInstance = monitor.PtInstance,
                    monitorName       = monitor.PtName,
                    desktopId         = desktopId.ToString("D"),
                    desktopName       = $"Escritorio {d + 1}",
                    layoutId          = layoutId ?? "",
                });
            }
        }
        return new { entries };
    }

    private object HandleCzeSetActiveLayout(JsonElement payload)
    {
        try
        {
            string ptInstance = payload.TryGetProperty("monitorPtInstance", out var mi) ? mi.GetString() ?? "" : "";
            string desktopIdStr = payload.TryGetProperty("desktopId", out var di) ? di.GetString() ?? "" : "";
            string layoutId = payload.TryGetProperty("layoutId", out var li) ? li.GetString() ?? "" : "";

            if (!Guid.TryParse(desktopIdStr, out Guid desktopId))
                return new { ok = false, error = "Invalid desktopId" };

            string key = WorkspaceLauncher.Core.CustomZoneEngine.Models.ActiveLayoutMap.MakeKey(ptInstance, desktopId);
            var config = ConfigManager.Instance.Config;

            if (string.IsNullOrEmpty(layoutId))
                config.CzeActiveLayouts.Remove(key);
            else
                config.CzeActiveLayouts[key] = layoutId;

            ConfigManager.Instance.Save();
            return new { ok = true };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    private void HandleCzeOpenCanvas(JsonElement payload)
    {
        string monitorHardwareId = payload.TryGetProperty("monitorHardwareId", out var mid) ? mid.GetString() ?? "" : "";
        string layoutId = payload.TryGetProperty("layoutId", out var lid) ? lid.GetString() ?? "" : "";
        bool isNew = payload.TryGetProperty("isNew", out var isNewEl) && isNewEl.GetBoolean();
        WorkspaceLauncher.Core.CustomZoneEngine.UI.ZoneEditorLauncher.Instance.OpenCanvas(monitorHardwareId, layoutId, isNew);
    }

    // ── Outgoing to JS ─────────────────────────────────────────────────────
    public void SendEvent(string eventName, object data)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { @event = eventName, data }, JsonOpts);
            _window.Dispatcher.Invoke(() =>
            {
                try { _webView.PostWebMessageAsJson(payload); }
                catch { /* WebView may be disposed */ }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bridge] SendEvent error: {ex.Message}");
        }
    }
}
