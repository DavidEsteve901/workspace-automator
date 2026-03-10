"use strict";

const { contextBridge, ipcRenderer } = require("electron");

/**
 * Secure contextBridge — exposes electronAPI to the renderer process.
 *
 * Renderer (React) usage:
 *   window.electronAPI.send('get_state')
 *   window.electronAPI.on('state_update', handler)
 *   window.electronAPI.invoke('list-windows')  // request/response
 */
contextBridge.exposeInMainWorld("electronAPI", {
  // ── Renderer → Main (fire-and-forget) ──────────────────────────────
  send: (action, payload = {}) => {
    ipcRenderer.send("bridge:action", { action, ...payload });
  },

  // ── Renderer → Main (request/response) ────────────────────────────
  invoke: (channel, ...args) => {
    return ipcRenderer.invoke(channel, ...args);
  },

  // ── Main → Renderer ──────────────────────────────────────────────────
  on: (eventName, handler) => {
    const listener = (_e, msg) => {
      if (msg?.event === eventName) handler(msg.data);
    };
    ipcRenderer.on("bridge:event", listener);
    // Return cleanup function
    return () => ipcRenderer.removeListener("bridge:event", listener);
  },

  // ── Window controls (custom frameless titlebar) ───────────────────────
  minimize: () => ipcRenderer.send("win:minimize"),
  maximize: () => ipcRenderer.send("win:maximize"),
  close: () => ipcRenderer.send("win:close"),

  // ── Environment ──────────────────────────────────────────────────────
  isElectron: true,
  isDev: process.env.ELECTRON_DEV === "1",
});
