'use strict';

const { spawn } = require('child_process');
const path      = require('path');
const fs        = require('fs');

const TAB_SEP = '--- NUEVA PESTAÑA ---';

// ── Browser paths ──────────────────────────────────────────────────────────
const BROWSER_PATHS = {
  msedge:  [
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  ],
  chrome:  [
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  ],
  firefox: ['C:\\Program Files\\Mozilla Firefox\\firefox.exe'],
  brave:   ['C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe'],
};

function resolveBrowser(browser) {
  if (!browser || browser === 'default') return null;
  const candidates = BROWSER_PATHS[browser] || [browser];
  return candidates.find(p => fs.existsSync(p)) || null;
}

// ── Launchers per item type ────────────────────────────────────────────────
function launchItem(item) {
  try {
    switch (item.type) {
      case 'exe':        return launchExe(item);
      case 'url':        return launchUrl(item);
      case 'ide':        return launchIde(item);
      case 'vscode':     return launchVsCode(item);
      case 'powershell': return launchPowerShell(item);
      case 'obsidian':   return launchObsidian(item);
      default:           return launchExe(item);
    }
  } catch (err) {
    console.error(`[Launcher] Error launching ${item.type} '${item.path}':`, err.message);
    return null;
  }
}

function launchExe(item) {
  if (!fs.existsSync(item.path)) {
    console.warn(`[Launcher] EXE not found: ${item.path}`);
    return null;
  }
  return spawn(item.path, [], {
    detached: true,
    stdio:    'ignore',
    cwd:      path.dirname(item.path),
    shell:    false,
  });
}

function launchUrl(item) {
  const urls    = (item.cmd || item.path).split(TAB_SEP).map(u => u.trim()).filter(Boolean);
  const browser = resolveBrowser(item.browser);

  if (!browser) {
    // Use default system browser via shell
    urls.forEach(url => spawn('cmd', ['/c', 'start', '', url], { detached: true, stdio: 'ignore' }));
    return null;
  }

  const args = ['--new-window', ...urls];
  return spawn(browser, args, { detached: true, stdio: 'ignore' });
}

function launchIde(item) {
  if (!item.ide_cmd) return null;
  return spawn(item.ide_cmd, [item.path], {
    detached: true,
    stdio:    'ignore',
    cwd:      item.path,
    shell:    true,
  });
}

function launchVsCode(item) {
  return spawn('code', [item.path], {
    detached: true,
    stdio:    'ignore',
    shell:    true,
  });
}

function launchPowerShell(item) {
  const cmds = (item.cmd || '').split(TAB_SEP).map(c => c.trim()).filter(Boolean);
  if (!cmds.length) return null;

  // Build Windows Terminal command: first tab + extra tabs
  const firstTab  = `-d "${item.path}" pwsh -NoExit -Command "${cmds[0].replace(/"/g, '\\"')}"`;
  const extraTabs = cmds.slice(1)
    .map(c => `; new-tab -d "${item.path}" pwsh -NoExit -Command "${c.replace(/"/g, '\\"')}"`)
    .join(' ');

  return spawn('wt.exe', [], {
    detached: true,
    stdio:    'ignore',
    shell:    true,
    argv0:    `wt ${firstTab}${extraTabs}`,
  });
}

function launchObsidian(item) {
  const vaultName = item.path.split(/[/\\]/).pop();
  const uri       = `obsidian://open?vault=${encodeURIComponent(vaultName)}`;
  spawn('cmd', ['/c', 'start', '', uri], { detached: true, stdio: 'ignore' });
  return null;
}

// ── Main launch orchestrator ───────────────────────────────────────────────
async function launchWorkspace(config, category, sendProgress) {
  const items = config.apps?.[category];
  if (!items?.length) {
    sendProgress('Sin apps en este workspace.', 0);
    return;
  }

  sendProgress(`Iniciando workspace: ${category}`, 0);
  config.last_category = category;

  const total = items.length;
  for (let i = 0; i < total; i++) {
    const item    = items[i];
    const pct     = Math.round(((i + 1) / total) * 85) + 5;
    const name    = getItemName(item);

    sendProgress(`Lanzando: ${name}`, pct);

    const delay = parseInt(item.delay, 10) || 0;
    if (delay > 0) await sleep(delay);

    const proc = launchItem(item);
    if (proc) proc.unref();

    // Small gap between launches to avoid overwhelming the system
    await sleep(400);
  }

  sendProgress('Workspace lanzado correctamente.', 100);
}

function getItemName(item) {
  switch (item.type) {
    case 'exe':        return path.basename(item.path, '.exe');
    case 'url':        { try { return new URL(item.path).hostname; } catch { return item.path; } }
    case 'ide':        return `${item.ide_cmd}: ${path.basename(item.path)}`;
    case 'vscode':     return `VSCode: ${path.basename(item.path)}`;
    case 'powershell': return `Terminal: ${path.basename(item.path)}`;
    case 'obsidian':   return `Obsidian: ${path.basename(item.path)}`;
    default:           return item.path;
  }
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

// ── IPC Registration ───────────────────────────────────────────────────────
function register(ipcMain, sendToRenderer) {
  // Lazy-load config handler reference
  ipcMain.on('bridge:action', async (event, msg) => {
    if (msg.action !== 'launch_workspace') return;

    const configHandler = require('./config.js');
    const config        = configHandler.register._getConfig?.() || {};
    const category      = msg.category || config.last_category || Object.keys(config.apps || {})[0];

    if (!category) {
      sendToRenderer('launch_progress', { status: 'error', message: 'No hay workspace seleccionado.', progress: 0 });
      return;
    }

    sendToRenderer('launch_progress', { status: 'launching', message: 'Iniciando...', progress: 0 });

    try {
      await launchWorkspace(config, category, (message, progress) => {
        sendToRenderer('launch_progress', { status: progress < 100 ? 'launching' : 'done', message, progress });
      });
    } catch (err) {
      sendToRenderer('launch_progress', { status: 'error', message: `Error: ${err.message}`, progress: 0 });
    }
  });
}

module.exports = { register };
