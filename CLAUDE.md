# CLAUDE.md — Memoria de Contexto de Larga Duración
> Optimizado para ingesta rápida por IA. Leer completo antes de tocar cualquier archivo.

---

## 1. CORE DEL PROYECTO

### Dos apps, un config
| App | Stack | Estado | Arranque |
|-----|-------|--------|---------|
| `launcher_pro.py` | Python + customtkinter | **LEGACY — NO MODIFICAR** | `python launcher_pro.py` |
| `WorkspaceLauncher/` | C# .NET 8 WPF + WebView2 + React/Vite | **ACTIVO** | `npm run dev` (desde `WorkspaceLauncher/`) |

Config compartida: `mis_apps_config_v2.json` (raíz del repo Y `WorkspaceLauncher/bin/Debug/.../`).

### Por qué .NET WPF (no Electron)
- `.NET 8 SDK` está instalado en la máquina. `electron` se probó y descartó (symlink issue en `electron-builder portable`).
- WPF hostea WebView2 que carga React. IPC vía JSON sobre `chrome.webview.postMessage`.
- Win32 nativo (P/Invoke) para todo: hooks, DWM, virtual desktops, SetWindowPos.

### Dev workflow
```bash
cd WorkspaceLauncher
npm run dev
# → Arranca Vite en :5173 (--strictPort, falla si ocupado) + dotnet watch run con hot reload
# Si falla por WorkspaceLauncher.exe en uso:
#   powershell -Command "Stop-Process -Name WorkspaceLauncher -Force"
```

---

## 2. MAPA DE INTELIGENCIA (archivos críticos)

```
WorkspaceLauncher/
├── Bridge/WebBridge.cs              ← IPC handler: TODOS los mensajes JS↔C#. 1254 líneas.
│                                       Fire-and-forget (postMessage) + invoke (requestId/response).
│
├── Core/Launcher/
│   ├── WorkspaceOrchestrator.cs     ← ★ CEREBRO. Launch/Restore/Clean. 3 sweeps finales.
│   ├── WindowDetector.cs            ← Detección por PID → heurística → scoring (min score=3).
│   ├── ProcessLauncher.cs           ← Launch por tipo: exe/url/ide/vscode/powershell/obsidian.
│   └── WorkspaceResolver.cs         ← Remapeo de monitores entre setups. Runtime-only (no guarda).
│
├── Core/FancyZones/
│   ├── FancyZonesReader.cs          ← Lee/escribe custom-layouts.json + applied-layouts.json.
│   ├── ZoneCalculator.cs            ← Matemática de zonas: grid (%) y canvas (ref+scale).
│   └── LayoutSyncer.cs              ← Inyecta layouts en PowerToys antes de cada launch.
│                                       Rescala canvas layouts a la resolución del monitor actual.
│
├── Core/NativeInterop/
│   ├── DwmHelper.cs                 ← ★ SetWindowPos con compensación DWM shadow. ForceFocus().
│   │                                   GetVisualBounds() = DWMWA_EXTENDED_FRAME_BOUNDS (sin shadow).
│   ├── VirtualDesktopManager.cs     ← COM para escritorios virtuales. Cache 600ms en GetDesktops().
│   │                                   Soporta Win11 22H2/24H2 + fallback teclado.
│   └── WindowManager.cs             ← EnumWindows, GetWindowRect (LÓGICO = incluye shadow).
│
├── Core/ZoneEngine/ZoneStack.cs     ← Dict (desktop,monitor,layout,zone)→[HWND]. Thread-safe.
│
└── frontend/src/
    ├── App.jsx                      ← Estado global React + routing main/config.
    └── api/bridge.js                ← Wrapper JS para postMessage + invoke con timeout 15s.
```

├── Core/Launcher/PipWatcher.cs          ← Detección PiP (ES+EN) + pinning COM real a todos escritorios.
│                                           Heurística fallback: clase Chrome_WidgetWin + WS_EX_TOPMOST + tamaño pequeño.
│
└── Core/NativeInterop/
    └── [PipWatcher.cs duplicado]         ← IGNORAR — stub vacío, reemplazado por Core/Launcher/PipWatcher.cs

**Archivos que NO importan para el flujo principal:**
`OSD/`, `SystemTray/TrayManager.cs`, `ZoneEngine/ZoneCycler.cs`, `HotkeyProcessor.cs`.

---

## 3. ARQUITECTURA: FLUJO DE LAUNCH

```
JS: bridge.launchWorkspace(category)
  → WebBridge.HandleLaunchWorkspace()
    → WorkspaceOrchestrator.LaunchWorkspaceAsync()

FASE 0: EnsureVirtualDesktopsAsync()    — crea escritorios faltantes vía COM
FASE 1: WorkspaceResolver.ResolveEnvironment()  — remapeo monitor en memoria (NO guarda)
FASE 2: LayoutSyncer.SyncForWorkspace() — inyecta layouts en PowerToys; rescala canvas layouts
FASE 3: Loop por escritorio (secuencial) → Loop por ítem (secuencial):
  a. PrepareDesktopAsync()             — SwitchToDesktop + verify loop ×5
  b. ProcessLauncher.Launch()          — spawn proceso
  c. WindowDetector:
       WaitForWindowAsync(PID, timeout)  ← tipo-específico: vscode=14s, url=12s, exe=8s
       → WaitForNewWindowAsync(heurística)
       → ScoreMatchBestWindow(fallback)
  d. GetReadinessDelay(tipo)           — espera: url=650ms, vscode=550ms, exe=150ms
  e. VerificaDesktopCorrecto + MoveWindowToDesktop si difiere
  f. DwmHelper.ApplyZoneRect(retries=4, silent=!isHere)
  g. ZoneStack.Register()
FASE 4: 3 FinalIntegritySweep() con delays:
  800ms → sweep(silent=true)  → 1200ms → sweep(silent=true) → 2000ms → sweep(silent=false)
  — Usa hwndsByItem (HWNDs tracked) en lugar de re-scoring
  — Compara con DwmHelper.GetVisualBounds() (NO GetWindowRect)
  — Tolerancias: pass1=30px, pass2=8px
```

---

## 4. REGLAS DE ORO (no romper)

### DWM Shadow — la regla más importante
- `GetWindowRect()` → coords **LÓGICAS** (incluye shadow invisible ~8px por lado)
- `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` → coords **VISUALES** (lo que el usuario ve)
- `SetWindowPos()` trabaja en LÓGICAS → hay que compensar con `CompensateForSetWindowPos()`
- Para comparar drift: **SIEMPRE usar `DwmHelper.GetVisualBounds()`**, nunca `GetWindowRect()`
- Si comparas lógico vs visual: diff de ~16px en width → siempre falla tolerancia=8px → bucle infinito

### ApplyZoneRect — precondiciones
- Restaurar **minimizado (IsIconic) Y maximizado (IsZoomed)** antes de SetWindowPos
- Ventana maximizada ignora silenciosamente SetWindowPos sin restaurar primero
- `silent=true` → SWP_NOACTIVATE|SWP_NOZORDER (no roba foco, no cambia escritorio activo)
- `silent=false` → SWP_SHOWWINDOW + ForceFocus (ALT hack + AttachThreadInput + TOPMOST toggle)

### WorkspaceResolver — NO auto-guardar
- `ResolveEnvironment()` modifica `item.Monitor` en memoria solamente
- **NUNCA llamar `ConfigManager.Instance.Save()` desde aquí**
- Si se guarda: config portable se corrompe al cambiar entre setups (casa↔trabajo↔portátil)

### VirtualDesktopManager.GetDesktops() — cache obligatorio
- Cache 600ms via `_desktopsCacheTime`. Invalidar en `CreateDesktop()`.
- Sin cache: 8 COM round-trips por validación (uno por ítem). COM ~10-30ms c/u.
- `_cachedDesktops` existía pero se escribía sin leerse — bug histórico ya corregido.

### VirtualDesktopManager — Pinning (PiP)
- `PinWindow(hwnd)`: requiere `IApplicationViewCollection.GetViewForHwnd` → `IVirtualDesktopPinnedApps.PinView`
- `_shell` (IServiceProvider) guardado en `TryInit22H2/24H2` — se reutiliza para QueryService de pinning
- `EnsurePinningServices()`: lazy init de `_viewCollection` + `_pinnedApps`, thread-safe via `_pinLock`
- GUIDs estables en todas las builds Win11:
  - `IApplicationViewCollection`: `1841C6D7-4F9D-42C0-AF41-8747538F10E5`
  - `IVirtualDesktopPinnedApps`: `4CE81583-1E4C-4632-A621-07A53543148F`

### LayoutSyncer — portabilidad canvas
- Canvas layouts tienen `ref-width`/`ref-height` de la máquina original
- Si monitor actual tiene resolución diferente → `RescaleCanvasLayoutInfo()` escala coords
- Grid layouts son independientes de resolución (son % del work area)
- Deduplicar por `(uuid, monitor)` — varios ítems pueden compartir mismo layout+monitor

### FancyZones — formato v2
- `applied-layouts.json` usa estructura `device: { monitor-instance, monitor, virtual-desktop }`
- UUIDs en PowerToys van con llaves: `{UUID-EN-MAYUSCULAS}`
- Matching monitor: primero por `monitor-instance` GUID, luego por `monitor` name

### Config schema
```json
{
  "apps": { "NombreWorkspace": [ AppItem ] },
  "last_category": "string",
  "category_order": ["string"],
  "applied_mappings": { "{uuid}_{monitor}": "layoutName" },
  "fz_layouts_cache": { "uuid-lowercase": { "uuid","name","type","info":{} } },
  "hotkeys": { "cycle_forward":"ctrl+alt+pagedown", ... },
  "pip_watcher_enabled": true,
  "fz_custom_path": null
}
```

### AppItem campos clave
```
type: exe|url|ide|vscode|powershell|obsidian
path: ruta/URL principal
cmd: URLs extra o tabs PS (sep: "--- NUEVA PESTAÑA ---")
monitor: nombre display (ej. "AUS2723 (4480x2520)") | "Por defecto"
desktop: "Escritorio N" | "Por defecto"
fancyzone: "NombreLayout - Zona N" | "Ninguna"
fancyzone_uuid: "{UUID-lowercase-sin-llaves}" | ""
delay: "500" (ms como string) | ""
```

---

## 5. HISTORIAL DE BUGS / FIXES

| # | Bug | Archivo | Fix |
|---|-----|---------|-----|
| 1 | `hwndsByItem` declarado nunca usado | `WorkspaceOrchestrator.cs:57` | Poblado en loop launch, pasado a sweeps |
| 2 | Drift detection: lógico vs visual | `WorkspaceOrchestrator.cs:FinalIntegritySweep` | Usa `DwmHelper.GetVisualBounds()` |
| 3 | Ventanas maximizadas ignoraban snap | `DwmHelper.cs:ApplyZoneRect` | `IsZoomed` → ShowWindow(RESTORE) |
| 4 | `WorkspaceResolver` auto-guardaba config | `WorkspaceResolver.cs:58-63` | Eliminado `ConfigManager.Save()` |
| 5 | Sin espera de readiness por tipo app | `WorkspaceOrchestrator.cs` | `GetReadinessDelay()` nuevo método |
| 6 | Timeout detección hardcodeado 6s | `WorkspaceOrchestrator.cs:LaunchOnlyAsync` | `GetDetectionTimeout()` por tipo |
| 7 | `GetDesktops()` sin cache → 8 COM calls | `VirtualDesktopManager.cs:124` | Cache 600ms + invalidación en CreateDesktop |
| 8 | Process huérfano en :5173 | `frontend/package.json` | `vite --port 5173 --strictPort` |
| 9 | Canvas layouts no escalaban en portabilidad | `LayoutSyncer.cs` | `RescaleCanvasLayoutInfo()` nuevo método |
| 10 | `LayoutSyncer` duplicaba inyecciones | `LayoutSyncer.cs` | Set de `(uuid,monitor)` procesados |
| 11 | `PinWindow()`/`IsWindowPinned()` eran stubs vacíos | `VirtualDesktopManager.cs:473` | Impl COM real: `IApplicationViewCollection` + `IVirtualDesktopPinnedApps` |
| 12 | PiP: solo títulos EN detectados | `PipWatcher.cs:17` | Añadidos "imagen en imagen", "imagen con imagen" (ES) + heurística clase browser |
| 13 | Layout detector siempre null en ItemDialog | `ItemDialog.jsx:102` | Monitor lookup buscaba `m.label` pero `form.monitor` guarda `m.ptName` |
| 14 | UUID activo no se grababa si layout no en cache | `WebBridge.cs:694` | `matchedLayoutUuid = rawUuid` aunque no esté en cache |

### Monitores activos en esta máquina
```
SDC41B6  2880x1800  PtInstance=4&1d653659&0&UID8388688  Primary=True   \\.\DISPLAY1
AUS2723  4480x2520  PtInstance=4&1d653659&0&UID28727    Primary=False  \\.\DISPLAY2
```
VirtualDesktops: 2 (Build24H2 COM).

---

## 6. PENDIENTES / BACKLOG

### Inmediato (próxima sesión)
- [ ] **Probar launch real** con el nuevo sistema de 3 sweeps + readiness delays
- [ ] **Probar PiP**: abrir Chrome/Edge PiP, verificar que en ~2s se ancla y permanece en todos los escritorios virtuales
- [ ] **Probar portabilidad**: desconectar AUS2723 → lanzar workspace → verificar remapeo a SDC41B6 SIN guardar
- [ ] **Canvas layout scaling**: crear item con canvas layout en AUS2723, verificar escala en pantalla única

### Deuda técnica media
- [ ] `HandleValidateWorkspace` muta `items` en memoria durante validación → debería clonar antes de `ResolveEnvironment`
- [ ] `FinalIntegritySweep` `silent=false` a 3s puede robar foco si usuario está trabajando. Evaluar si molesta en uso real.
- [ ] `_cachedDesktops` TTL 600ms: durante launch largo el desktop puede cambiar. Invalidar cache al inicio de `LaunchWorkspaceAsync`.
- [ ] `Core/NativeInterop/PipWatcher.cs` es un stub huérfano — eliminar o vaciar para evitar confusión con `Core/Launcher/PipWatcher.cs`

### Features pendientes (no urgente)
- [ ] UI para layouts FancyZones activos (bridge `get_fz_status` existe, falta componente React)
- [ ] Perfiles de entorno: "config@casa" vs "config@trabajo" sin corromper config base
- [ ] `CleanWorkspace` por escritorio virtual (ahora solo por scoring global)

---

## 7. IPC BRIDGE CHEATSHEET

**JS → C# (fire-and-forget):**
`get_state`, `launch_workspace`, `restore_workspace`, `clean_workspace`,
`save_item`, `delete_item`, `move_item`, `add_category`, `delete_category`,
`rename_category`, `move_category`, `save_config`, `set_last_category`,
`window_minimize/maximize/close/drag`

**JS → C# (invoke, espera respuesta, timeout 15s):**
`list_monitors`, `list_desktops`, `list_fancyzones`, `get_fz_status`,
`validate_workspace`, `resolve_monitor_conflicts`, `list_windows`,
`get_windows_to_clean`, `close_windows`, `open_file_dialog`,
`change_layout_assignment`, `get_config_path`, `change_config_path`

**C# → JS (eventos push):**
`state_update` → estado completo, `launch_progress` → {status,message,progress%},
`system_log` → debug, `error` → mensaje de error, `invoke_response` → respuesta a invoke

---

## 8. ENTORNO DE DESARROLLO

- OS: Windows 11 Pro 10.0.26200 (Build 26200 = 24H2+)
- .NET: SDK 10.0.103 + target net8.0-windows
- Node: con `concurrently`, `cross-env`, `wait-on` en raíz; React+Vite en `frontend/`
- `dotnet watch run` con hot reload activo — cambios en C# se aplican sin reiniciar
- `WorkspaceLauncher.bat` → lanza el exe publicado (no dev mode)

**Kill limpio para reiniciar dev:**
```powershell
Stop-Process -Name WorkspaceLauncher -Force
# luego npm run dev
```

---

## 9. PROTOCOLO DE MANTENIMIENTO DE ESTE ARCHIVO

**Al final de CADA sesión de trabajo** Claude debe actualizar este CLAUDE.md con:

1. **Sección 5 (Historial de Bugs)**: añadir filas para cada bug resuelto en la sesión.
2. **Sección 6 (Pendientes)**: marcar `[x]` ítems completados, añadir nuevos, mover deuda técnica nueva.
3. **Sección 2 (Mapa)**: actualizar si hay archivos nuevos o cambia la responsabilidad de alguno.
4. **Sección 4 (Reglas de Oro)**: añadir regla si se descubrió un gotcha importante.
5. **Sección 8 (Entorno)**: actualizar si cambia SDK, Node, build target, etc.

**Objetivo**: que al abrir un nuevo chat, leyendo solo este archivo, Claude tenga suficiente contexto para continuar sin preguntas y sin repetir errores pasados. Mantenerlo denso pero no redundante. Eliminar ítems del historial que ya no aporten info nueva (los bugs ya no son relevantes después de >3 sesiones si la fix está estabilizada).

**Skill disponible**: `/update-context` — ejecutar al final de la sesión para actualizar este archivo.

