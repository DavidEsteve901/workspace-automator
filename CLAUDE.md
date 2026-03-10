# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Running the App

```bash
python launcher_pro.py
```

Dependencies (Windows only):
```bash
python -m pip install -r requirements.txt
```

Requirements: `customtkinter`, `pyvda`, `pywin32`, `pynput`, `pygetwindow`

## Architecture

This is a **single-file Windows desktop application** (`launcher_pro.py`, ~4700 lines) built with `customtkinter` (CTk). There are no modules, no build steps, and no tests.

### Key Global State

- `APP_DIR`: Directory of the script (or executable if compiled). The app `os.chdir`s into it on startup.
- `THEME`: Dict defining the "DARK PRO" color palette used throughout all UI widgets.
- Config persisted to `mis_apps_config_v2.json` in `APP_DIR`.

### Class Overview

| Class | Role |
|---|---|
| `GlobalHookManager` | Installs `WH_MOUSE_LL` / `WH_KEYBOARD_LL` Win32 hooks in a dedicated thread to intercept/suppress mouse X1/X2 buttons. Callbacks wired to zone-cycling logic in `DevLauncherApp`. |
| `DevLauncherApp` | Main `ctk.CTk` window. Contains all application logic: UI construction, config loading/saving, FancyZones integration, virtual desktop management, zone stacking, hotkey system, launch engine. |
| `AdvancedItemDialog` | Full item configuration dialog. Handles all item types: `exe`, `url`, `ide`, `vscode`, `powershell`, `obsidian`. Zone/monitor/desktop pickers live here. |
| `AssignLayoutsDialog` | UI for mapping FancyZones layout UUIDs to monitors per virtual desktop. |
| `CleanWorkspaceDialog` | Lists open windows and allows bulk-closing them, with filters (launched by this app, matching config, on a specific desktop). |
| `HotkeysEditorDialog` / `RecordHotkeyDialog` | Edit and record keyboard/mouse hotkey bindings stored in config. |
| `RecoverySelectionDialog` | After a workspace recovery scan, shows unmatched window intents and lets the user manually match them to open windows before snapping. |
| `AddIDEDialog` / `AddMultiWebDialog` | Simplified quick-add dialogs for IDE projects and multi-tab browser sessions. |

### Item Types

Each workspace category holds a list of items. Supported `type` values:

- `exe` — Launch an arbitrary `.exe`
- `url` — Open one or more URLs (multi-tab via `--- NUEVA PESTAÑA ---` separator in `cmd`) in a chosen browser
- `ide` — Open a directory in a configured IDE (e.g. `antigravity` command)
- `vscode` — Open a directory in VS Code
- `powershell` — Open Windows Terminal with one or more tabs/commands
- `obsidian` — Open an Obsidian vault

All items share: `monitor`, `desktop`, `fancyzone`, `delay`, and optionally `fancyzone_uuid`.

### FancyZones Integration

- Layout definitions are read from `%LOCALAPPDATA%\Microsoft\PowerToys\FancyZones\`.
- Layouts are cached in `mis_apps_config_v2.json` under `fz_layouts_cache` (keyed by UUID) for portability across PowerToys restarts.
- `applied_mappings` maps `"{layout_uuid}_{monitor_device}"` strings to human-readable layout names for the current environment.
- Zone positioning is done mathematically via `_calculate_zone_rect` using the layout grid definition — no reliance on FancyZones runtime snapping.
- `_inject_layout_to_powertoys` writes layout assignments directly into PowerToys config files.

### Virtual Desktop Management

Uses `pyvda` (`AppView`, `VirtualDesktop`, `get_virtual_desktops`). The launch engine calls `_ensure_required_virtual_desktops` before launching to create any missing desktops, then switches desktops before placing each window.

### Zone Stacking & Hotkey Engine

- `zone_stacks`: Dict keyed by `(desktop_guid, monitor_device, layout_uuid, zone_index)` → list of HWNDs.
- `_start_global_hotkeys` sets up the hotkey system using both `pynput` (keyboard) and `GlobalHookManager` (mouse X1/X2).
- Cycling methods: `_cycle_zone_forward`, `_cycle_zone_backward`, `_cycle_desktop_forward`, `_cycle_desktop_backward`.
- `_detect_zone_for_window` performs "fuzzy" matching — identifies which zone a window belongs to by comparing its current rect against calculated zone rects.
- A PiP watcher thread (`_pip_watcher_loop`) periodically pins small floating windows to all virtual desktops.

### Launch Engine

`launch_workspace()` is the main entry point:
1. Syncs FancyZones layouts (`_sync_fz_layouts_for_workspace`)
2. Ensures virtual desktops exist
3. Spawns threads per item via `_launch_and_snap_intent`
4. Performs a final integrity sweep 1.5s after launch to reposition any drifted windows

### Config Schema (`mis_apps_config_v2.json`)

```json
{
  "apps": { "<category_name>": [ /* item objects */ ] },
  "last_category": "...",
  "applied_mappings": { "{uuid}_{monitor}": "layout_name" },
  "fz_layouts_cache": { "uuid": { "uuid", "name", "type", "info" } },
  "hotkeys": { "cycle_forward": "ctrl+alt+pagedown", ... },
  "pip_watcher_enabled": true
}
```
