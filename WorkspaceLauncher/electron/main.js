'use strict';

const { app, BrowserWindow, ipcMain, Tray, Menu, nativeImage, shell } = require('electron');
const path  = require('path');
const fs    = require('fs');

// ── Import IPC handlers ────────────────────────────────────────────────────
const configHandler   = require('./ipc/config.js');
const launcherHandler = require('./ipc/launcher.js');
const fzHandler       = require('./ipc/fancyzones.js');
const winMgrHandler   = require('./ipc/windowMgr.js');
const hooksHandler    = require('./ipc/hooks.js');

const IS_DEV = process.env.ELECTRON_DEV === '1';

let mainWindow = null;
let tray       = null;

// ── App config ─────────────────────────────────────────────────────────────
app.setAppUserModelId('com.workspacelauncher.app');
app.disableHardwareAcceleration();  // Avoid GPU crashes on some setups

// ── Window creation ────────────────────────────────────────────────────────
function createWindow() {
  mainWindow = new BrowserWindow({
    width:           1100,
    height:          700,
    minWidth:        800,
    minHeight:       500,
    backgroundColor: '#0f0f23',
    frame:           false,          // Frameless — custom titlebar in React
    titleBarStyle:   'hidden',
    show:            false,          // Show after ready-to-show
    icon:            getIconPath(),
    webPreferences: {
      preload:           path.join(__dirname, 'preload.js'),
      contextIsolation:  true,
      nodeIntegration:   false,
      sandbox:           false,
      devTools:          IS_DEV,
    },
  });

  // Load content
  if (IS_DEV) {
    mainWindow.loadURL('http://localhost:5173');
    mainWindow.webContents.openDevTools({ mode: 'detach' });
  } else {
    const indexPath = path.join(__dirname, '..', 'frontend', 'dist', 'index.html');
    mainWindow.loadFile(indexPath);
  }

  // Show when fully loaded (avoids white flash)
  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
  });

  // Minimize to tray instead of closing
  mainWindow.on('close', (e) => {
    if (!app.isQuitting) {
      e.preventDefault();
      mainWindow.hide();
      tray?.displayBalloon?.({
        title:   'Workspace Launcher',
        content: 'Minimizado al área de notificación.',
        iconType: 'info',
      });
    }
  });

  // Block navigation to external URLs
  mainWindow.webContents.on('will-navigate', (e, url) => {
    if (IS_DEV && url.startsWith('http://localhost')) return;
    e.preventDefault();
  });

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });
}

// ── System Tray ────────────────────────────────────────────────────────────
function createTray() {
  const iconPath = getIconPath();
  const icon     = iconPath ? nativeImage.createFromPath(iconPath) : nativeImage.createEmpty();

  tray = new Tray(icon.resize({ width: 16, height: 16 }));
  tray.setToolTip('Workspace Launcher');

  const menu = Menu.buildFromTemplate([
    { label: 'Abrir',                 click: () => { mainWindow?.show(); mainWindow?.focus(); } },
    { label: 'Lanzar último workspace', click: () => { mainWindow?.show(); sendToRenderer('tray_launch', {}); } },
    { type:  'separator' },
    { label: 'Salir',                 click: () => { app.isQuitting = true; app.quit(); } },
  ]);

  tray.setContextMenu(menu);
  tray.on('double-click', () => { mainWindow?.show(); mainWindow?.focus(); });
}

// ── IPC registration ───────────────────────────────────────────────────────
function registerIpcHandlers() {
  configHandler.register(ipcMain, sendToRenderer);
  launcherHandler.register(ipcMain, sendToRenderer);
  fzHandler.register(ipcMain, sendToRenderer);
  winMgrHandler.register(ipcMain, sendToRenderer);
  hooksHandler.register(ipcMain, sendToRenderer);

  // Window control (custom titlebar)
  ipcMain.on('win:minimize', () => mainWindow?.minimize());
  ipcMain.on('win:maximize', () => {
    if (mainWindow?.isMaximized()) mainWindow.unmaximize();
    else mainWindow?.maximize();
  });
  ipcMain.on('win:close', () => mainWindow?.close());
}

// ── Helpers ────────────────────────────────────────────────────────────────
function sendToRenderer(channel, data) {
  if (mainWindow?.webContents && !mainWindow.webContents.isDestroyed()) {
    mainWindow.webContents.send('bridge:event', { event: channel, data });
  }
}

function getIconPath() {
  // Look for icon next to exe in production, or in parent dir in dev
  const candidates = [
    path.join(process.resourcesPath || '', 'launcher_icon1.ico'),
    path.join(app.getAppPath(), '..', 'launcher_icon1.ico'),
    path.join(__dirname, '..', '..', 'launcher_icon1.ico'),
  ];
  return candidates.find(p => fs.existsSync(p)) || null;
}

// ── App lifecycle ──────────────────────────────────────────────────────────
app.whenReady().then(() => {
  createWindow();
  createTray();
  registerIpcHandlers();
});

app.on('window-all-closed', (e) => {
  // Keep alive in tray
  e.preventDefault();
});

app.on('activate', () => {
  mainWindow?.show();
});

app.on('before-quit', () => {
  app.isQuitting = true;
  hooksHandler.cleanup();
});
