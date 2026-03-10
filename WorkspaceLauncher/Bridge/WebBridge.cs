using System.Diagnostics;
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
public sealed class WebBridge
{
    private readonly CoreWebView2 _webView;
    private readonly MainWindow   _window;

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

    public WebBridge(CoreWebView2 webView, MainWindow window)
    {
        _webView = webView;
        _window  = window;
    }

    public void Initialize()
    {
        _webView.WebMessageReceived += OnWebMessageReceived;

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
            string? reqId = root.TryGetProperty("requestId", out var r) ? r.GetString() : null;

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

                case "add_category":
                    await HandleAddCategory(payload);
                    break;

                case "delete_category":
                    await HandleDeleteCategory(payload);
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
                    _window.Dispatcher.Invoke(() => _window.Hide());
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
                    ReplyInvoke(reqId, HandleListFancyZones());
                    break;

                case "get_fz_status":
                    ReplyInvoke(reqId, HandleGetFzStatus());
                    break;

                case "change_layout_assignment":
                    ReplyInvoke(reqId, await HandleChangeLayoutAssignment(payload));
                    break;

                case "open_file_dialog":
                    var dialogResult = await HandleOpenFileDialog(payload);
                    ReplyInvoke(reqId, dialogResult);
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
        SendEvent("state_update", new
        {
            categories     = config.Apps,
            lastCategory   = config.LastCategory,
            hotkeys        = config.Hotkeys,
            pipWatcher     = config.PipWatcherEnabled,
            fzLayoutsCache = config.FzLayoutsCache,
            fzCustomPath   = config.FzCustomPath,
            fzDetectedPath = FancyZonesReader.FzBasePath,
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

    private async Task HandleAddCategory(JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name)) return;
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(name))
            config.Apps[name] = [];
        config.LastCategory = name;
        await ConfigManager.Instance.SaveAsync();
        HandleGetState();
    }

    private async Task HandleDeleteCategory(JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var config = ConfigManager.Instance.Config;
        config.Apps.Remove(name);
        if (config.LastCategory == name)
            config.LastCategory = config.Apps.Keys.FirstOrDefault() ?? "";
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
            id         = m.Handle,
            deviceName = m.DeviceName,
            hardwareId = m.HardwareId,
            ptName     = m.PtName,
            ptInstance = m.PtInstance,
            name       = m.Name,
            label      = $"{m.Name}{(m.IsPrimary ? " ★" : "")}",
            isPrimary  = m.IsPrimary,
        }).ToArray();
    }

    private object HandleListDesktops()
    {
        try
        {
            var vdm      = VirtualDesktopManager.Instance;
            var desktops = vdm.GetDesktops();
            var current  = vdm.GetCurrentDesktopId();
            
            if (desktops.Count == 0)
            {
                // Absolute fallback: always show at least one desktop
                return new[] { new { id = Guid.Empty.ToString(), name = "Escritorio 1", isCurrent = true } };
            }

            return desktops.Select((id, idx) => new
            {
                id   = id.ToString().ToLowerInvariant(),
                name = $"Escritorio {idx + 1}",
                isCurrent = current.HasValue && current.Value == id
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
            FancyZonesReader.SyncCacheFromDisk();
            var applied = FancyZonesReader.ReadAppliedLayouts();
            var layoutsCache = ConfigManager.Instance.Config.FzLayoutsCache;

            var availableLayouts = layoutsCache.Values.Select(l => {
                // Determine zone count from layout info
                int zones = 0;
                try {
                    if (l.Info.TryGetProperty("cell-child-map", out var map))
                        zones = map.GetArrayLength();
                    else if (l.Info.TryGetProperty("rows", out var rows) && l.Info.TryGetProperty("columns", out var cols))
                        zones = rows.GetInt32() * cols.GetInt32();
                    else if (l.Info.TryGetProperty("zones", out var zArray))
                        zones = zArray.GetArrayLength();
                } catch { }

                return new {
                    uuid = l.Uuid,
                    name = l.Name,
                    type = l.Type,
                    zoneCount = zones > 0 ? zones : 1,
                    info = l.Info
                };
            }).ToArray();

            return new
            {
                applied = applied,
                layouts = availableLayouts
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleListFancyZones error: {ex.Message}");
            return new { applied = new List<object>(), layouts = new List<object>() };
        }
    }

    private object HandleGetFzStatus()
    {
        try
        {
            FancyZonesReader.SyncCacheFromDisk();
            var applied = FancyZonesReader.ReadAppliedLayouts();
            var layoutsCache = ConfigManager.Instance.Config.FzLayoutsCache;
            var monitors = MonitorManager.GetActiveMonitors();

            var vdm = VirtualDesktopManager.Instance;
            var desktopIds = vdm.GetDesktops();
            var currentDesktop = vdm.GetCurrentDesktopId();

            var desktops = desktopIds.Count > 0
                ? desktopIds.Select((id, idx) => new
                {
                    id = id.ToString().ToLowerInvariant(),
                    name = $"Escritorio {idx + 1}",
                    isCurrent = currentDesktop.HasValue && currentDesktop.Value == id
                }).ToArray()
                : new[] { new { id = Guid.Empty.ToString(), name = "Escritorio 1", isCurrent = true } };

            var availableLayouts = layoutsCache.Values.Select(l =>
            {
                int zones = 0;
                try
                {
                    if (l.Info.TryGetProperty("cell-child-map", out var map))
                    {
                        var allCells = new HashSet<int>();
                        for (int rr = 0; rr < map.GetArrayLength(); rr++)
                            foreach (var cell in map[rr].EnumerateArray())
                                allCells.Add(cell.GetInt32());
                        zones = allCells.Count;
                    }
                    else if (l.Info.TryGetProperty("zones", out var zArray))
                        zones = zArray.GetArrayLength();
                }
                catch { }

                return new
                {
                    uuid = l.Uuid,
                    name = l.Name,
                    type = l.Type,
                    zoneCount = zones > 0 ? zones : 1,
                    info = l.Info
                };
            }).ToArray();

            // Build a map: for each monitor+desktop, find the active layout
            var statusEntries = new List<object>();
            foreach (var mon in monitors)
            {
                foreach (var dk in desktops)
                {
                    // Find matching applied entry
                    object? matchedLayout = null;
                    string matchedLayoutUuid = "";

                    foreach (var appliedEntry in applied)
                    {
                        // Serialize the anonymous object to extract its properties
                        var json = JsonSerializer.Serialize(appliedEntry);
                        using var doc = JsonDocument.Parse(json);
                        var ae = doc.RootElement;

                        string? aeInstance = ae.TryGetProperty("instance", out var inst) ? inst.GetString() : null;
                        string? aeMonitorName = ae.TryGetProperty("monitorName", out var mn) ? mn.GetString() : null;
                        string? aeDesktopId = ae.TryGetProperty("desktopId", out var dkid) ? dkid.GetString() : null;
                        string? aeLayoutUuid = ae.TryGetProperty("layoutUuid", out var lu) ? lu.GetString() : null;

                        // ── Monitor matching: PtInstance is the most reliable unique key ──
                        // PtInstance = "4&1d653659&0&UID8388688" matches instance = "4&1d653659&0&UID8388688"
                        bool monMatch = false;
                        if (!string.IsNullOrEmpty(aeInstance) && !string.IsNullOrEmpty(mon.PtInstance) 
                            && aeInstance == mon.PtInstance)
                        {
                            monMatch = true;
                        }
                        // Fallback: match by monitor model name (less precise for identical monitors)
                        else if (!string.IsNullOrEmpty(aeMonitorName) && !string.IsNullOrEmpty(mon.PtName) 
                            && aeMonitorName == mon.PtName)
                        {
                            monMatch = true;
                        }

                        // ── Desktop matching: compare trimmed lowercase GUIDs ──
                        string dkIdNorm = dk.id.Trim('{', '}').ToLowerInvariant();
                        string aeDkNorm = (aeDesktopId ?? "").Trim('{', '}').ToLowerInvariant();
                        
                        bool dkMatch = string.IsNullOrEmpty(aeDkNorm) ||
                                       aeDkNorm == "00000000-0000-0000-0000-000000000000" ||
                                       aeDkNorm == dkIdNorm;

                        if (monMatch && dkMatch && !string.IsNullOrEmpty(aeLayoutUuid))
                        {
                            var layout = layoutsCache.Values.FirstOrDefault(l =>
                                l.Uuid.Trim('{', '}').Equals(aeLayoutUuid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
                            if (layout != null)
                            {
                                matchedLayout = new { uuid = layout.Uuid, name = layout.Name };
                                matchedLayoutUuid = layout.Uuid;
                                Logger.Info($"[FzStatus] Matched: {mon.PtName}({mon.PtInstance}) + {dk.name} → {layout.Name}");
                            }
                            break;
                        }
                    }

                    statusEntries.Add(new
                    {
                        monitorId = mon.Handle,
                        monitorLabel = $"{mon.Name}{(mon.IsPrimary ? " ★" : "")}",
                        monitorPtName = mon.PtName,
                        monitorPtInstance = mon.PtInstance,
                        monitorHardwareId = mon.HardwareId,
                        desktopId = dk.id,
                        desktopName = dk.name,
                        desktopIsCurrent = dk.isCurrent,
                        activeLayout = matchedLayout,
                        activeLayoutUuid = matchedLayoutUuid
                    });
                }
            }

            return new
            {
                entries = statusEntries,
                layouts = availableLayouts,
                monitors = monitors.Select(m => new
                {
                    id = m.Handle,
                    deviceName = m.DeviceName,
                    hardwareId = m.HardwareId,
                    ptName = m.PtName,
                    ptInstance = m.PtInstance,
                    name = m.Name,
                    label = $"{m.Name}{(m.IsPrimary ? " ★" : "")}",
                    isPrimary = m.IsPrimary,
                }).ToArray(),
                desktops
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"[WebBridge] HandleGetFzStatus error: {ex.Message}");
            return new { entries = new List<object>(), layouts = new List<object>(), monitors = new List<object>(), desktops = new List<object>() };
        }
    }

    private async Task<object> HandleChangeLayoutAssignment(JsonElement payload)
    {
        try
        {
            string monitorInstance = payload.TryGetProperty("monitorInstance", out var mi) ? mi.GetString() ?? "" : "";
            string monitorName = payload.TryGetProperty("monitorName", out var mn) ? mn.GetString() ?? "" : "";
            string? desktopId = payload.TryGetProperty("desktopId", out var di) ? di.GetString() : null;
            string layoutUuid = payload.TryGetProperty("layoutUuid", out var lu) ? lu.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(layoutUuid))
                return new { success = false, error = "No layout UUID provided" };

            bool ok = FancyZonesReader.InjectLayoutByDevice(monitorInstance, monitorName, desktopId, layoutUuid);
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
                    Title = "Seleccionar carpeta"
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
