/**
 * Desktop bridge — exclusive for WebView2 (C#/.NET).
 */

const IS_WEBVIEW2 = typeof window !== "undefined" && !!window.chrome?.webview;

let _invokeCounter = 0;
const _pendingInvokes = new Map(); // requestId → { resolve, reject, timer }

function generateRequestId() {
  return `req_${++_invokeCounter}_${Date.now()}`;
}

export function postMessage(action, payload = {}) {
  if (IS_WEBVIEW2) {
    window.chrome.webview.postMessage({ action, payload });
  } else {
    console.warn(`[Bridge] WebView2 not detected. Action '${action}' ignored.`);
  }
}

async function invoke(channel, payload = {}) {
  if (IS_WEBVIEW2) {
    return new Promise((resolve, reject) => {
      const requestId = generateRequestId();
      const timer = setTimeout(() => {
        if (_pendingInvokes.has(requestId)) {
          _pendingInvokes.delete(requestId);
          reject(new Error(`[Bridge] Invoke timeout: ${channel}`));
        }
      }, 15000);

      _pendingInvokes.set(requestId, { resolve, reject, timer });
      window.chrome.webview.postMessage({
        action: channel,
        payload,
        requestId,
      });
    });
  }
  return Promise.resolve(null);
}

export const bridge = {
  getState: () => postMessage("get_state"),
  launchWorkspace: (category) => postMessage("launch_workspace", { category }),
  restoreWorkspace: (category) => postMessage("restore_workspace", { category }),
  cleanWorkspace: (category) => postMessage("clean_workspace", { category }),
  setLastCategory: (category) => postMessage("set_last_category", { category }),
  saveItem: (category, index, item) =>
    postMessage("save_item", { category, index, payload: item }),
  deleteItem: (category, index) =>
    postMessage("delete_item", { category, index }),
  moveItem: (category, from, to) =>
    postMessage("move_item", { category, from, to }),
  moveCategory: (from, to) =>
    postMessage("move_category", { from, to }),
  addCategory: (name) => postMessage("add_category", { name }),
  deleteCategory: (name) => postMessage("delete_category", { name }),
  renameCategory: (oldName, newName) => postMessage("rename_category", { oldName, newName }),
  saveConfig: (config) => postMessage("save_config", config),
  saveFzPath: (path) => postMessage("save_fz_path", { path }),
  setHotkeysEnabled: (enabled) => postMessage("set_hotkeys_enabled", { enabled }),
  startDrag: () => postMessage("window_drag"),

  listWindows: () => invoke("list_windows"),
  listDesktops: () => invoke("list_desktops"),
  listMonitors: () => invoke("list_monitors"),
  listFancyZones: () => invoke("list_fancyzones"),
  getFzStatus: () => invoke("get_fz_status"),
  changeLayoutAssignment: (monitorInstance, monitorName, desktopId, layoutUuid, layoutType) =>
    invoke("change_layout_assignment", { monitorInstance, monitorName, desktopId, layoutUuid, layoutType }),
  openFileDialog: (opts = {}) => invoke("open_file_dialog", opts),
  getWindowsToClean: (category) => invoke("get_windows_to_clean", { category }),
  closeWindows: (hwnds) => invoke("close_windows", { hwnds }),
  validateWorkspace: (category) => invoke("validate_workspace", { category }),
  syncWorkspaceLayouts: (layoutUuids) => invoke("sync_workspace_layouts", { layoutUuids }),
  resolveMonitorConflicts: (category, resolutions) => invoke("resolve_monitor_conflicts", { category, resolutions }),
  getConfigPath: () => invoke("get_config_path"),
  openConfigFolder: () => postMessage("open_config_folder"),
  changeConfigPath: (path) => invoke("change_config_path", { path }),

  minimize: () => postMessage("window_minimize"),
  maximize: () => postMessage("window_maximize"),
  close: () => postMessage("window_close"),
};

const listeners = {};

export function onEvent(eventName, handler) {
  if (!listeners[eventName]) listeners[eventName] = new Set();
  listeners[eventName].add(handler);
}

export function offEvent(eventName, handler) {
  listeners[eventName]?.delete(handler);
}

function dispatch(eventName, data) {
  listeners[eventName]?.forEach((h) => h(data));
}

if (IS_WEBVIEW2) {
  window.chrome.webview.addEventListener("message", (event) => {
    const msg = event.data;
    if (!msg || !msg.event) return;

    if (msg.event === "invoke_response" && msg.data?.requestId) {
      const pending = _pendingInvokes.get(msg.data.requestId);
      if (pending) {
        clearTimeout(pending.timer);
        _pendingInvokes.delete(msg.data.requestId);
        pending.resolve(msg.data.result);
      }
      return;
    }

    dispatch(msg.event, msg.data);
  });
}
