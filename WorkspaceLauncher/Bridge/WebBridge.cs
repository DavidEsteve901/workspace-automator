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
        var monitors = WindowManager.GetMonitors();
        return monitors.Select((m, idx) => new
        {
            id     = idx + 1,
            name   = m.Name,
            label  = $"Pantalla {idx + 1} [{m.Name}]",
            workArea = new { left = m.WorkArea.Left, top = m.WorkArea.Top, right = m.WorkArea.Right, bottom = m.WorkArea.Bottom }
        }).ToArray();
    }

    private object HandleListDesktops()
    {
        var vdm      = VirtualDesktopManager.Instance;
        var desktops = vdm.GetDesktops();
        return desktops.Select((id, idx) => new
        {
            id   = id.ToString(),
            name = $"Escritorio {idx + 1}",
        }).ToArray();
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
