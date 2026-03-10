'use strict';

const fs   = require('fs');
const path = require('path');

const FZ_BASE = path.join(
  process.env.LOCALAPPDATA || '',
  'Microsoft', 'PowerToys', 'FancyZones'
);

const APPLIED_LAYOUTS = path.join(FZ_BASE, 'applied-layouts.json');
const CUSTOM_LAYOUTS  = path.join(FZ_BASE, 'custom-layouts.json');

function readJson(filePath) {
  try { return JSON.parse(fs.readFileSync(filePath, 'utf8')); }
  catch { return null; }
}

function writeJson(filePath, data) {
  fs.writeFileSync(filePath, JSON.stringify(data, null, 2), 'utf8');
}

function readCustomLayouts() {
  const data = readJson(CUSTOM_LAYOUTS);
  if (!data) return {};
  const result = {};
  for (const layout of data['custom-layouts'] || []) {
    const uuid = layout.uuid?.replace(/[{}]/g, '').toLowerCase();
    if (uuid) result[uuid] = layout;
  }
  return result;
}

function readAppliedLayouts() {
  const data = readJson(APPLIED_LAYOUTS);
  if (!data) return {};
  const result = {};
  for (const entry of data['applied-layouts'] || []) {
    const deviceId   = entry['device-id'];
    const layoutUuid = entry['applied-layout']?.uuid?.replace(/[{}]/g, '').toLowerCase();
    if (deviceId && layoutUuid) result[deviceId] = layoutUuid;
  }
  return result;
}

function injectLayoutAssignment(deviceId, layoutUuid) {
  const data = readJson(APPLIED_LAYOUTS);
  if (!data) return false;

  const arr    = data['applied-layouts'] || [];
  const upper  = `{${layoutUuid.toUpperCase()}}`;
  const entry  = arr.find(e => e['device-id'] === deviceId);

  if (entry) {
    entry['applied-layout'].uuid = upper;
  } else {
    arr.push({ 'device-id': deviceId, 'applied-layout': { uuid: upper, type: 'custom' } });
  }

  data['applied-layouts'] = arr;
  writeJson(APPLIED_LAYOUTS, data);
  return true;
}

function syncForWorkspace(items, appliedMappings) {
  for (const item of items) {
    if (!item.fancyzone_uuid) continue;
    if (item.monitor === 'Por defecto' || item.fancyzone === 'Ninguna') continue;
    injectLayoutAssignment(item.monitor, item.fancyzone_uuid);
  }
  console.log('[FancyZones] Layouts synchronized.');
}

// ── IPC Registration ───────────────────────────────────────────────────────
function register(ipcMain, sendToRenderer) {
  ipcMain.on('bridge:action', (event, msg) => {
    if (msg.action !== 'sync_fancyzones') return;

    const configHandler = require('./config.js');
    const config        = configHandler.register._getConfig?.() || {};
    const category      = msg.category || config.last_category;
    const items         = config.apps?.[category] || [];

    try {
      syncForWorkspace(items, config.applied_mappings || {});
      sendToRenderer('fz_synced', { ok: true });
    } catch (err) {
      sendToRenderer('fz_synced', { ok: false, error: err.message });
    }
  });
}

module.exports = { register, readCustomLayouts, readAppliedLayouts, syncForWorkspace };
