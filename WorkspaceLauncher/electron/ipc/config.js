"use strict";

const fs = require("fs");
const path = require("path");
const { app, dialog, BrowserWindow } = require("electron");

const CONFIG_FILE = "mis_apps_config_v2.json";

function getConfigPath() {
  const candidates = [
    path.join(process.resourcesPath || "", CONFIG_FILE),
    path.join(app.getPath("exe"), "..", CONFIG_FILE),
    path.join(app.getAppPath(), "..", "..", CONFIG_FILE), // dev: repo root
    path.join(app.getAppPath(), "..", CONFIG_FILE),
  ];
  const existing = candidates.find((p) => fs.existsSync(p));
  return existing || path.join(app.getPath("userData"), CONFIG_FILE);
}

function loadConfig() {
  const p = getConfigPath();
  try {
    const raw = fs.readFileSync(p, "utf8");
    return JSON.parse(raw);
  } catch {
    return {
      apps: {},
      last_category: "",
      applied_mappings: {},
      fz_layouts_cache: {},
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
      pip_watcher_enabled: true,
    };
  }
}

function saveConfig(config) {
  const p = getConfigPath();
  fs.writeFileSync(p, JSON.stringify(config, null, 4), "utf8");
}

// ── IPC Registration ───────────────────────────────────────────────────
function register(ipcMain, sendToRenderer) {
  let config = loadConfig();

  function refreshState() {
    sendToRenderer("state_update", {
      categories: config.apps,
      lastCategory: config.last_category,
      hotkeys: config.hotkeys,
      pipWatcher: config.pip_watcher_enabled,
    });
  }

  // ── Fire-and-forget actions ──────────────────────────────────────────
  ipcMain.on("bridge:action", (event, msg) => {
    switch (msg.action) {
      case "get_state":
        config = loadConfig();
        refreshState();
        break;

      case "set_last_category":
        config.last_category = msg.category || "";
        saveConfig(config);
        break;

      case "add_category": {
        const name = msg.name?.trim();
        if (name && !config.apps[name]) config.apps[name] = [];
        saveConfig(config);
        refreshState();
        break;
      }

      case "delete_category": {
        const name = msg.name;
        delete config.apps[name];
        if (config.last_category === name)
          config.last_category = Object.keys(config.apps)[0] || "";
        saveConfig(config);
        refreshState();
        break;
      }

      case "save_item": {
        const { category, index, payload } = msg;
        if (!category) break;
        if (!config.apps[category]) config.apps[category] = [];
        const list = config.apps[category];
        if (index >= 0 && index < list.length) list[index] = payload;
        else list.push(payload);
        saveConfig(config);
        refreshState();
        break;
      }

      case "delete_item": {
        const { category, index } = msg;
        if (config.apps[category]) {
          config.apps[category].splice(index, 1);
          saveConfig(config);
        }
        refreshState();
        break;
      }

      case "move_item": {
        const { category, from, to } = msg;
        const list = config.apps[category];
        if (
          !list ||
          from < 0 ||
          to < 0 ||
          from >= list.length ||
          to >= list.length
        )
          break;
        const [item] = list.splice(from, 1);
        list.splice(to, 0, item);
        saveConfig(config);
        refreshState();
        break;
      }

      case "save_config": {
        if (msg.hotkeys) config.hotkeys = msg.hotkeys;
        if (msg.pipWatcherEnabled !== undefined)
          config.pip_watcher_enabled = msg.pipWatcherEnabled;
        saveConfig(config);
        break;
      }
    }
  });

  // ── Request/response handlers (ipcMain.handle) ───────────────────────

  // Native file/folder dialog
  ipcMain.handle("open-file-dialog", async (_event, opts = {}) => {
    const win = BrowserWindow.getFocusedWindow();
    const properties = opts.isFolder ? ["openDirectory"] : ["openFile"];

    const result = await dialog.showOpenDialog(win, {
      title: opts.isFolder ? "Seleccionar carpeta" : "Seleccionar archivo",
      properties,
      filters: opts.filters || [
        { name: "Ejecutables", extensions: ["exe"] },
        { name: "Todos los archivos", extensions: ["*"] },
      ],
    });

    if (result.canceled || !result.filePaths.length) return null;
    return result.filePaths[0];
  });

  // Expose config loader for other IPC handlers
  register._getConfig = () => config;
}

module.exports = { register };
