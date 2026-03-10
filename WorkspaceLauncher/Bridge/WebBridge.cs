using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WorkspaceLauncher.Core.Config;
using WorkspaceLauncher.Core.Launcher;
using WorkspaceLauncher.Core.OSD;

namespace WorkspaceLauncher.Bridge;

/// <summary>
/// Bidirectional JSON bridge between WebView2 (React) and C# backend.
///
/// JS → C#: chrome.webview.postMessage({ action: "...", payload: {...} })
/// C# → JS: CoreWebView2.PostWebMessageAsJson(JSON)
/// </summary>
public sealed class WebBridge
{
    private readonly CoreWebView2 _webView;
    private readonly MainWindow   _window;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false,
    };

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
            SendEvent("launch_progress", new { status = "launching", message = msg, progress = pct });
    }

    // ── Incoming from JS ───────────────────────────────────────────────────
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root   = doc.RootElement;
            string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";

            switch (action)
            {
                case "get_state":
                    await HandleGetState();
                    break;

                case "launch_workspace":
                    string cat = root.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                    await HandleLaunchWorkspace(cat);
                    break;

                case "save_item":
                    await HandleSaveItem(root);
                    break;

                case "delete_item":
                    await HandleDeleteItem(root);
                    break;

                case "move_item":
                    await HandleMoveItem(root);
                    break;

                case "add_category":
                    string catName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    await HandleAddCategory(catName);
                    break;

                case "delete_category":
                    string catDel = root.TryGetProperty("name", out var nd) ? nd.GetString() ?? "" : "";
                    await HandleDeleteCategory(catDel);
                    break;

                case "save_config":
                    await HandleSaveConfig(root);
                    break;

                case "set_last_category":
                    string lastCat = root.TryGetProperty("category", out var lc) ? lc.GetString() ?? "" : "";
                    HandleSetLastCategory(lastCat);
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

    // ── Handlers ───────────────────────────────────────────────────────────
    private Task HandleGetState()
    {
        var config = ConfigManager.Instance.Config;
        SendEvent("state_update", new
        {
            categories    = config.Apps,
            lastCategory  = config.LastCategory,
            hotkeys       = config.Hotkeys,
            pipWatcher    = config.PipWatcherEnabled,
        });
        return Task.CompletedTask;
    }

    private async Task HandleLaunchWorkspace(string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            category = ConfigManager.Instance.Config.LastCategory;
        }
        if (string.IsNullOrEmpty(category)) return;

        ConfigManager.Instance.Config.LastCategory = category;
        await ConfigManager.Instance.SaveAsync();

        await WorkspaceOrchestrator.Instance.LaunchWorkspaceAsync(category);
    }

    private async Task HandleSaveItem(JsonElement root)
    {
        string category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    idx      = root.TryGetProperty("index",    out var i) ? i.GetInt32() : -1;
        var    payload  = root.TryGetProperty("payload",  out var p) ? p : default;

        if (string.IsNullOrEmpty(category)) return;

        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(category))
            config.Apps[category] = [];

        var item = JsonSerializer.Deserialize<AppItem>(payload.GetRawText(), JsonOpts);
        if (item == null) return;

        if (idx >= 0 && idx < config.Apps[category].Count)
            config.Apps[category][idx] = item;
        else
            config.Apps[category].Add(item);

        await ConfigManager.Instance.SaveAsync();
        await HandleGetState();
    }

    private async Task HandleDeleteItem(JsonElement root)
    {
        string category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    idx      = root.TryGetProperty("index",    out var i) ? i.GetInt32() : -1;

        var config = ConfigManager.Instance.Config;
        if (config.Apps.TryGetValue(category, out var items) && idx >= 0 && idx < items.Count)
        {
            items.RemoveAt(idx);
            await ConfigManager.Instance.SaveAsync();
        }
        await HandleGetState();
    }

    private async Task HandleMoveItem(JsonElement root)
    {
        string category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        int    from     = root.TryGetProperty("from",     out var f) ? f.GetInt32() : -1;
        int    to       = root.TryGetProperty("to",       out var t) ? t.GetInt32() : -1;

        var config = ConfigManager.Instance.Config;
        if (!config.Apps.TryGetValue(category, out var items)) return;
        if (from < 0 || to < 0 || from >= items.Count || to >= items.Count) return;

        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);

        await ConfigManager.Instance.SaveAsync();
        await HandleGetState();
    }

    private async Task HandleAddCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var config = ConfigManager.Instance.Config;
        if (!config.Apps.ContainsKey(name))
            config.Apps[name] = [];
        await ConfigManager.Instance.SaveAsync();
        await HandleGetState();
    }

    private async Task HandleDeleteCategory(string name)
    {
        var config = ConfigManager.Instance.Config;
        config.Apps.Remove(name);
        if (config.LastCategory == name)
            config.LastCategory = config.Apps.Keys.FirstOrDefault() ?? "";
        await ConfigManager.Instance.SaveAsync();
        await HandleGetState();
    }

    private async Task HandleSaveConfig(JsonElement root)
    {
        var config = ConfigManager.Instance.Config;
        if (root.TryGetProperty("hotkeys", out var hk))
            config.Hotkeys = JsonSerializer.Deserialize<HotkeyConfig>(hk.GetRawText(), JsonOpts) ?? config.Hotkeys;
        if (root.TryGetProperty("pipWatcherEnabled", out var pip))
            config.PipWatcherEnabled = pip.GetBoolean();
        await ConfigManager.Instance.SaveAsync();
    }

    private void HandleSetLastCategory(string category)
    {
        ConfigManager.Instance.Config.LastCategory = category;
        ConfigManager.Instance.Save();
    }

    // ── Outgoing to JS ─────────────────────────────────────────────────────
    public void SendEvent(string eventName, object data)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { @event = eventName, data }, JsonOpts);
            _webView.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bridge] SendEvent error: {ex.Message}");
        }
    }
}
