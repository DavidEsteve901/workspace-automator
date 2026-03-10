/**
 * Desktop bridge — works in both Electron and browser dev mode.
 *
 * In Electron:  window.electronAPI (injected by preload.js via contextBridge)
 * In browser:   falls back to mock data + console logging
 */

const IS_ELECTRON = typeof window !== "undefined" && !!window.electronAPI;

// ── Outgoing (Renderer → Main, fire-and-forget) ────────────────────────
export function postMessage(action, payload = {}) {
  if (IS_ELECTRON) {
    window.electronAPI.send(action, payload);
  } else {
    console.log("[Bridge mock] →", action, payload);
    _mockRespond(action, payload);
  }
}

// ── Outgoing (Renderer → Main, request/response) ───────────────────────
async function invoke(channel, ...args) {
  if (IS_ELECTRON) {
    return window.electronAPI.invoke(channel, ...args);
  } else {
    return _mockInvoke(channel, ...args);
  }
}

export const bridge = {
  getState: () => postMessage("get_state"),
  launchWorkspace: (category) => postMessage("launch_workspace", { category }),
  setLastCategory: (category) => postMessage("set_last_category", { category }),
  saveItem: (category, index, payload) =>
    postMessage("save_item", { category, index, payload }),
  deleteItem: (category, index) =>
    postMessage("delete_item", { category, index }),
  moveItem: (category, from, to) =>
    postMessage("move_item", { category, from, to }),
  addCategory: (name) => postMessage("add_category", { name }),
  deleteCategory: (name) => postMessage("delete_category", { name }),
  saveConfig: (config) => postMessage("save_config", config),

  // ── Auto-detect APIs (request/response) ─────────────────────────────
  listWindows: () => invoke("list-windows"),
  listDesktops: () => invoke("list-desktops"),
  openFileDialog: (opts = {}) => invoke("open-file-dialog", opts),

  // Window controls (frameless titlebar)
  minimize: () => (IS_ELECTRON ? window.electronAPI.minimize() : null),
  maximize: () => (IS_ELECTRON ? window.electronAPI.maximize() : null),
  close: () => (IS_ELECTRON ? window.electronAPI.close() : null),
};

// ── Incoming (Main → Renderer) ────────────────────────────────────────
const listeners = {};
const cleanups = [];

export function onEvent(eventName, handler) {
  if (!listeners[eventName]) listeners[eventName] = new Set();
  listeners[eventName].add(handler);

  if (IS_ELECTRON) {
    const cleanup = window.electronAPI.on(eventName, handler);
    if (typeof cleanup === "function") cleanups.push(cleanup);
  }
}

export function offEvent(eventName, handler) {
  listeners[eventName]?.delete(handler);
}

function dispatch(eventName, data) {
  listeners[eventName]?.forEach((h) => h(data));
}

// ── Dev mode mock ─────────────────────────────────────────────────────
export const MOCK_STATE = {
  categories: {
    Desarrollo: [],
    Navegación: [],
    "ViClient - SetUp home": [
      {
        type: "ide",
        path: "C:\\Proyectos\\backend",
        ide_cmd: "antigravity",
        monitor: "Pantalla 1 [AUS2723]",
        desktop: "Escritorio 1",
        fancyzone: "Entera - Zona 1",
        delay: "0",
      },
      {
        type: "exe",
        path: "C:\\Postman\\Postman.exe",
        monitor: "Pantalla 2 [SDC41B6]",
        desktop: "Escritorio 2",
        fancyzone: "Entera - Zona 1",
        delay: "0",
      },
      {
        type: "url",
        path: "https://jira.example.com",
        cmd: "https://jira.example.com",
        browser: "msedge",
        browser_display: "Microsoft Edge",
        monitor: "Pantalla 2 [SDC41B6]",
        desktop: "Escritorio 1",
        fancyzone: "Entera - Zona 1",
        delay: "0",
      },
      {
        type: "powershell",
        path: "C:\\Proyectos\\backend",
        cmd: "npm run dev --- NUEVA PESTAÑA --- npm run db:migrate",
        monitor: "Pantalla 1 [AUS2723]",
        desktop: "Escritorio 2",
        fancyzone: "Division izq mas grande - Zona 2",
        delay: "0",
      },
      {
        type: "obsidian",
        path: "C:\\OneDrive\\Desarrollo",
        monitor: "Pantalla 1 [AUS2723]",
        desktop: "Escritorio 1",
        fancyzone: "Entera - Zona 1",
        delay: "0",
      },
    ],
    Ocio: [],
  },
  lastCategory: "ViClient - SetUp home",
  hotkeys: {
    cycle_forward: "ctrl+alt+pagedown",
    cycle_backward: "ctrl+alt+pageup",
    mouse_cycle_fwd: "alt+x1",
    mouse_cycle_bwd: "alt+x2",
    desktop_cycle_fwd: "x1",
    desktop_cycle_bwd: "x2",
    util_reload_layouts: "ctrl+alt+l",
    _zone_cycle_enabled: true,
    _desktop_cycle_enabled: true,
  },
  pipWatcher: true,
};

let _mockState = JSON.parse(JSON.stringify(MOCK_STATE));

// Mock for invoke-style calls
async function _mockInvoke(channel, ...args) {
  await new Promise((r) => setTimeout(r, 200)); // simulate latency

  switch (channel) {
    case "list-windows":
      return [
        { hwnd: 1001, title: "Google Chrome", processName: "chrome.exe" },
        { hwnd: 1002, title: "Visual Studio Code", processName: "Code.exe" },
        {
          hwnd: 1003,
          title: "Windows Terminal",
          processName: "WindowsTerminal.exe",
        },
        { hwnd: 1004, title: "Microsoft Edge", processName: "msedge.exe" },
        { hwnd: 1005, title: "Obsidian", processName: "Obsidian.exe" },
      ];
    case "list-desktops":
      return [
        { id: "1", name: "Escritorio 1" },
        { id: "2", name: "Escritorio 2" },
        { id: "3", name: "Escritorio 3" },
      ];
    case "open-file-dialog":
      return "C:\\FakeApp\\example.exe"; // mock path
    default:
      return null;
  }
}

function _mockRespond(action, payload) {
  setTimeout(() => {
    switch (action) {
      case "get_state":
        dispatch("state_update", {
          categories: _mockState.categories,
          lastCategory: _mockState.lastCategory,
          hotkeys: _mockState.hotkeys,
          pipWatcher: _mockState.pipWatcher,
        });
        break;
      case "launch_workspace": {
        const items = _mockState.categories[payload.category] || [];
        items.forEach((item, i) => {
          const pct = Math.round(((i + 1) / items.length) * 85) + 5;
          setTimeout(() => {
            dispatch("launch_progress", {
              status: "launching",
              message: `Lanzando: ${payload.category}`,
              progress: pct,
            });
          }, i * 500);
        });
        setTimeout(
          () => {
            dispatch("launch_progress", {
              status: "done",
              message: "Workspace lanzado.",
              progress: 100,
            });
          },
          items.length * 500 + 300,
        );
        break;
      }
      case "add_category":
        if (payload.name && !_mockState.categories[payload.name])
          _mockState.categories[payload.name] = [];
        dispatch("state_update", {
          categories: _mockState.categories,
          lastCategory: payload.name,
          hotkeys: _mockState.hotkeys,
          pipWatcher: _mockState.pipWatcher,
        });
        break;
      case "delete_category":
        delete _mockState.categories[payload.name];
        dispatch("state_update", {
          categories: _mockState.categories,
          lastCategory: _mockState.lastCategory,
          hotkeys: _mockState.hotkeys,
          pipWatcher: _mockState.pipWatcher,
        });
        break;
      case "save_item": {
        const list = _mockState.categories[payload.category] || [];
        if (payload.index >= 0 && payload.index < list.length)
          list[payload.index] = payload.payload;
        else list.push(payload.payload);
        _mockState.categories[payload.category] = list;
        dispatch("state_update", {
          categories: _mockState.categories,
          lastCategory: _mockState.lastCategory,
          hotkeys: _mockState.hotkeys,
          pipWatcher: _mockState.pipWatcher,
        });
        break;
      }
      case "delete_item": {
        const list = _mockState.categories[payload.category] || [];
        list.splice(payload.index, 1);
        dispatch("state_update", {
          categories: _mockState.categories,
          lastCategory: _mockState.lastCategory,
          hotkeys: _mockState.hotkeys,
          pipWatcher: _mockState.pipWatcher,
        });
        break;
      }
    }
  }, 30);
}
