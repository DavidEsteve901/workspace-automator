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

### CustomZoneEngine — arquitectura de ventanas
- `OverlayWindow(blocking=true)` cubre exactamente el `WorkArea` (rcWork) de cada monitor
  - Background `#01000000` (alpha=1): visible pero captura hit-test → bloquea el escritorio
  - Sin `WS_EX_TRANSPARENT` → el OS envía todos los clicks a la overlay, no a ventanas detrás
  - `WS_EX_LAYERED | WS_EX_TOOLWINDOW` (no aparece en Alt-Tab ni barra de tareas)
  - `blocking=false` (default) = drag-preview, click-through → mantiene compat con `ZoneInteractionManager`
- Jerarquía Owner: `OverlayWindow` → `ZoneEditorManagerWindow` (Admin) / `ZoneCanvasEditorWindow` (Editing)
  - Owner garantiza que el hijo siempre queda encima del padre
  - Los overlays duran desde `OpenManager()` hasta `CloseAll()` → escritorio siempre bloqueado
- Zonas CZE en unidades base-10000 (int): 10000 = 100% del WorkArea. Portabilidad total sin Sync
  - `ToPixelRect`: `Left = workArea.Left + X * workArea.Width / 10000` (int)
  - Al guardar: React envía `Math.round(frac * 10000)`. Al cargar: `int / 10000` en React
  - `RefWidth`/`RefHeight` en `CzeLayoutEntry` → badge `∝` si resolución difiere (cosmético, no bloquea)
- Estado CZE: `Closed → Admin → Editing → Admin → Closed`
  - `WebBridge.BroadcastCzeState()` envía `cze_state_changed` a todos los bridges activos (WeakRef list)

### ZoneEngine vs FancyZonesSyncEnabled — dos flags, una decisión
- `ZoneEngine` (config key `zone_engine`) → determina qué motor se usa para el snapping de ventanas: `"fancyzones"` (default) | `"custom"` (CZE).
- `FancyZonesSyncEnabled` (config key `fz_sync_enabled`) → determina si se escriben en disco los archivos de PowerToys FancyZones durante el launch.
- Regla: `LayoutSyncer.SyncForWorkspace` solo debe ejecutarse si AMBOS son positivos (`FancyZonesSyncEnabled=true` Y `ZoneEngine="fancyzones"`).
- `ConfigManager.IsFancyZonesSyncActive` combina los dos. `ZoneEngineManager.IsFancyZonesActive` solo evalúa el motor.
- `ResolveZoneRect` / `RegisterInZoneStack` / `FinalIntegritySweep` comprueban `engineIsCze` para despachar al motor correcto. Si `ZoneEngine="custom"` pero el ítem solo tiene `FancyzoneUuid` (sin `CzeLayoutId`), la función devuelve null (sin zona) — correcto, el ítem no está configurado para CZE.
- `WebBridge.HandleValidateWorkspace` todavía usa solo `FancyZonesSyncEnabled` como discriminador (archivo no modificado en esta sesión). Para corrección completa, cambiar ese flag por `ConfigManager.Instance.IsFancyZonesSyncActive`.

### ItemDialog — separación de responsabilidades de carga
- **Siempre** cargar (sin condición): `listMonitors()` → `rawMonitors`, `listDesktops()` → `rawDesktops`, `listFancyZones()` → `fzLayouts`, `czeGetLayouts()` → `czeLayouts`
- **Solo cuando `fzSyncEnabled=true`**: `getFzStatus()` → `fzStatus` (detección de layout activo por monitor+escritorio, indicador verde)
- `fzSyncEnabled` controla únicamente si el app escribe en `applied-layouts.json` al lanzar — **nunca** debe ocultar layouts del editor ni vaciar los dropdowns de monitor/escritorio
- Polling cada 8s refresca `rawMonitors`, `fzLayouts`, y `fzStatus` (cuando aplica) en background

### LayoutSyncer — portabilidad canvas
- Canvas layouts tienen `ref-width`/`ref-height` de la máquina original
- Si monitor actual tiene resolución diferente → `RescaleCanvasLayoutInfo()` escala coords
- Grid layouts son independientes de resolución (son % del work area)
- Deduplicar por `(uuid, monitor)` — varios ítems pueden compartir mismo layout+monitor

### FancyZones — formato v2
- `applied-layouts.json` usa estructura `device: { monitor-instance, monitor, virtual-desktop }`
- UUIDs en PowerToys van con llaves: `{UUID-EN-MAYUSCULAS}`
- `"monitor-instance"` se almacena con `&` escapado como `\u0026` en JSON → al parsear queda `4&1d653659&0&UID28727`
- Pueden existir múltiples entradas para el mismo monitor con distintos `monitor-number` (conexiones históricas) → el código actualiza TODAS al inyectar, lo cual es correcto
- `"serial-number": "0"` es el valor que PowerToys escribe para monitores sin serial EDID real (SDC41B6 en esta máquina)
- Monitores distintos pueden compartir la misma `monitor-instance` si se conectan al mismo puerto físico (HSD4241 comparte `UID28727` con AUS2723) → usar `matchQuality` (nameMatch > instMatch) como tiebreaker

### FancyZones — bridge.changeLayoutAssignment — firma correcta
```js
bridge.changeLayoutAssignment(monitorInstance, monitorName, monitorSerial, desktopId, layoutUuid, layoutType?)
//  ← monitorPtInstance      ← monitorPtName   ← monitorSerial  ← desktopId   ← newLayoutUuid
```
Orden CRÍTICO — en el pasado estaba swapeado (desktopId↔monitorSerial, layoutUuid missing) → escrituras con `type=blank` y `virtual-desktop=<layoutUUID>`.

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
| 15 | `handleChangeLayout` en FzStatusModal pasaba args en orden incorrecto | `ConfigPanel.jsx:217` | Faltaba `monitorSerial`; `desktopId` y `layoutUuid` swapeados → escrituras basura en applied-layouts.json |
| 16 | Monitor "fantasma" con misma PnP instance sobreescribía layout correcto | `WebBridge.cs:HandleGetFzStatus` | Añadido `bestMatchQuality` — entry instMatch-only (q=50) no puede overridear nameMatch+instMatch (q=160) |
| 17 | `OverlayWindow` era click-through (`WS_EX_TRANSPARENT`) → no bloqueaba el escritorio | `OverlayWindow.xaml.cs` | Nuevo param `blocking=true`: sin `WS_EX_TRANSPARENT`, background `#01000000` (alpha=1 captura hit-test) |
| 18 | `CzeZoneEntry` guardaba coords como `double` 0-1 → no portables entre resoluciones | `Models.cs`, `CZEZone.cs` | Cambio a `int` base-10000; `ToPixelRect` usa `X * width / 10000` |
| 19 | `CzeLayoutEntry` no registraba resolución de referencia → badge de adaptación imposible | `Models.cs` | Añadido `RefWidth`/`RefHeight`; `HandleListMonitors` expone `workArea`; badge `∝` en LayoutCard |
| 20 | `ZoneEditorManagerWindow` y canvas eran ventanas independientes → manager podía quedar detrás | `ZoneEditorLauncher.cs` | Jerarquía Owner: `OverlayWindow` (raíz) → `Manager`/`Canvas` como owned. `ZoneEditorLauncher` tiene máquina de estados `Closed/Admin/Editing` |
| 21 | No había forma de notificar estado CZE a todos los WebBridge activos | `WebBridge.cs` | Añadido registro estático `_allBridges` (WeakReference) + `BroadcastCzeState()`; estado accesible con `cze_get_state` |
| 22 | `MonitorManager.GetActiveMonitors()` dejaba `Name="Pantalla N"` cuando WMI fallaba → lookups por nombre fallaban silenciosamente | `MonitorManager.cs` | Inicializar `Name=""`. Condición `DeviceString` solo requiere `IsNullOrEmpty(Name)`. Fallback chain explícito: WMI → DeviceString → PtName → deviceName → "Pantalla N" |
| 23 | `LayoutSyncer.SyncForWorkspace` ignoraba `FancyZonesSyncEnabled` y `ZoneEngine` → inyectaba layouts FZ incluso con sync desactivado o con motor CZE | `LayoutSyncer.cs` | Guard al inicio: retorna si `!FancyZonesSyncEnabled` o `ZoneEngine=="custom"`. Llama `FancyZonesReader.InvalidateCaches()` antes del sync para flush de caché obsoleta |
| 24 | `FancyZonesReader` no exponía forma de invalidar caché → datos obsoletos tras toggle de sync | `FancyZonesReader.cs` | Añadido `InvalidateCaches()` público que resetea timestamps y listas en memoria |
| 25 | `FinalIntegritySweep` solo procesaba ítems con zona FZ (`Fancyzone != "Ninguna"`) → ítems CZE nunca verificados en sweeps | `WorkspaceOrchestrator.cs` | Condición reemplazada por `hasFzZone`/`hasCzeZone` condicionada a `ZoneEngine` activo |
| 26 | `ResolveZoneRect`/`RegisterInZoneStack` siempre elegían CZE si había `CzeLayoutId` aunque `ZoneEngine="fancyzones"` → motor incorrecto al cambiar de CZE a FZ | `WorkspaceOrchestrator.cs` | Ambas funciones comprueban `engineIsCze = config.ZoneEngine=="custom"` antes de elegir ruta |
| 27 | Sistema de tematización: tema claro y color de acento no se aplicaban en gestor/control/canvas del editor de zonas | `App.css`, `App.jsx`, `ZoneEditorModal.jsx`, `ZoneEditorControlWindow.xaml(.cs)` | (a) `App.css` `:root` sobreescribía `[data-theme="light"]` de `index.css` → añadido bloque `[data-theme="light"]` en `App.css` para `--fz-*` vars. (b) `applyTheme()` no actualizaba `--fz-accent*` → añadido setProperty de `--fz-accent`, `--fz-accent-hover/dim/glow/low`. (c) `ZoneEditorControlWindow` tenía `UseImmersiveDarkMode(hwnd, true)` hardcoded, sin `DefaultBackgroundColor`, URL hardcodeada → lee `ConfigManager.ThemeMode`, configura background y usa `WL_DEV_URL`. (d) `modalStyle color:'white'` sobreescribía todo el texto → cambiado a `var(--fz-text)`. |
| 27 | `ZoneEngineManager` sin API pública de estado → otros módulos leían `config.ZoneEngine` ad-hoc, inconsistente | `ZoneEngineManager.cs` | Añadidos `IsFancyZonesActive` e `IsCzeActive` como propiedades estáticas |
| 28 | `ConfigManager` sin discriminador combinado → WebBridge usaba solo `FancyZonesSyncEnabled` ignorando `ZoneEngine` | `ConfigManager.cs` | Añadida propiedad `IsFancyZonesSyncActive = FancyZonesSyncEnabled && ZoneEngine=="fancyzones"` |
| 29 | `zonesToGrid` retornaba `initialGrid()` para layouts de una zona → zonas parciales (foco, CZE canvas) perdían posición al reconstruit la cuadrícula | `ZoneEditorHooks.js` | Eliminado early-return `zones.length===1`. El algoritmo ya produce 1×1 para zona full-screen y multi-celda para zonas parciales |
| 30 | `ItemDialog` no mostraba monitores, escritorios ni layouts cuando `fzSyncEnabled=false` — todo derivaba de `fzStatus` que se bloquea con el flag | `ItemDialog.jsx` | Arquitectura desacoplada: `loadRawEnv()` carga monitores/escritorios siempre; `loadFzLayouts()` carga layouts FZ siempre vía `listFancyZones()`; `loadFzStatus()` solo para detección de layout activo (indicador verde). `fzSyncEnabled` ya no oculta layouts del editor — solo controla si el app escribe en PowerToys al lanzar |
| 31 | `isFzRunning` en `HandleGetFzStatus` devolvía false aunque PowerToys estuviera en ejecución — solo buscaba `PowerToys.FancyZones` y `FancyZones`, no el proceso principal `PowerToys` | `WebBridge.cs` | Añadido `Process.GetProcessesByName("PowerToys")` al check — builds modernas de PowerToys embeben FancyZones en el proceso principal |
| 32 | `SyncModal` recibía `fzSyncEnabled={fzSyncEnabled}` (variable undefined en ese scope) | `App.jsx` | Corregido a `fzSyncEnabled={state.fzSyncEnabled}` |
| 33 | Sistema de tematización no existía — fondo e ícono de acento no personalizables | múltiples | Implementado: `ThemeMode`/`AccentColor` en `AppConfig`; `GetWindowsAccentColor()` en `DwmHelper.cs`; `get_theme_config` invoke en `WebBridge`; `applyTheme()` + `[data-theme]` en React; sección Apariencia en `ConfigPanel` |
| 34 | `ZoneEditorManagerWindow` usaba `UseImmersiveDarkMode(hwnd, true)` hardcoded → título siempre oscuro aunque el usuario pusiera tema claro | `ZoneEditorManagerWindow.xaml.cs` | Lee `ConfigManager.Config.ThemeMode` en `OnSourceInitialized` y en el init del WebView2; actualiza `DefaultBackgroundColor` y `Background` del Window |
| 35 | `ZoneCanvas.jsx` y `ZoneRect.jsx` usaban `--fz-accent` (variable inexistente), RGBA hardcodeados de cyan, y `color:'white'` — rompían con acento personalizado y con tema claro | `ZoneCanvas.jsx`, `ZoneRect.jsx` | Reemplazado todo por `var(--accent)`, `var(--accent-dim)`, `var(--text-primary)`, `var(--text-muted)`. Añadido `text-shadow` para legibilidad sobre desktop transparente |
| 36 | Cambios de tema/acento en ConfigPanel no propagaban a ventanas ZoneEditor — `HandleSaveConfig` llamaba `this.HandleGetState()` (solo su propio bridge), los demás bridges nunca recibían `state_update` | `WebBridge.cs` | Añadido `BroadcastStateUpdate()` estático que llama `HandleGetState()` en todos los bridges registrados. `HandleSaveConfig` ahora usa `BroadcastStateUpdate()`. Añadido mensaje `update_window_theme` para actualizar titlebar DWM en tiempo real. `applyTheme()` en React llama `bridge.updateWindowTheme(isDark)` después de cada cambio de tema. |

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
- [ ] **Probar CZE completo**: hotkey → overlays bloquean escritorio → manager abre como hijo → editar → guardar → vuelve a admin
- [ ] **Verificar badge ∝**: guardar layout en AUS2723, abrir manager en SDC41B6, confirmar que aparece el indicador de adaptación
- [ ] **Verificar toggle FZ sync**: activar/desactivar `fz_sync_enabled` → validar que `LayoutSyncer` respeta la bandera y no inyecta al desactivar
- [ ] **Verificar `WebBridge.HandleValidateWorkspace`**: cambiar su discriminador de `config.FancyZonesSyncEnabled` a `ConfigManager.Instance.IsFancyZonesSyncActive` para corregir el detector de conflictos cuando `ZoneEngine` y `FancyZonesSyncEnabled` difieren (archivo WebBridge.cs pendiente de modificar)
- [ ] **Probar monitores en ItemDialog**: abrir ítem en modo CZE (`fzSyncEnabled=false`) → confirmar que el select de monitor muestra los monitores activos (vía `rawMonitors` de `listMonitors()`)
- [ ] **Probar detección PowerToys**: activar FZ sync con PowerToys en background → confirmar `isFzRunning=true` y sin mensaje de error en el indicador

### Deuda técnica media
- [ ] `HandleValidateWorkspace` muta `items` en memoria durante validación → debería clonar antes de `ResolveEnvironment`
- [ ] `FinalIntegritySweep` `silent=false` a 3s puede robar foco si usuario está trabajando. Evaluar si molesta en uso real.
- [ ] `_cachedDesktops` TTL 600ms: durante launch largo el desktop puede cambiar. Invalidar cache al inicio de `LaunchWorkspaceAsync`.
- [ ] `Core/NativeInterop/PipWatcher.cs` es un stub huérfano — eliminar o vaciar para evitar confusión con `Core/Launcher/PipWatcher.cs`

### Features pendientes (no urgente)
- [x] UI para layouts FancyZones activos — `FzStatusModal` en `ConfigPanel.jsx` completo y funcional
- [x] Sistema de tematización — Modo Claro/Oscuro + color de acento personalizable + lectura de accent color de Windows
- [ ] Perfiles de entorno: "config@casa" vs "config@trabajo" sin corromper config base
- [ ] `CleanWorkspace` por escritorio virtual (ahora solo por scoring global)
- [ ] Añadir clases CSS `engine-toggle-group` / `engine-btn` que se referencian en `ConfigPanel.jsx` pero no existen en ningún CSS

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
`change_layout_assignment`, `get_config_path`, `change_config_path`,
`get_theme_config` → `{themeMode, accentColor, windowsAccentColor}`,
`cze_get_layouts`, `cze_save_layout`, `cze_delete_layout`, `cze_get_active_layouts`,
`cze_set_active_layout`, `cze_get_state`

**C# → JS (eventos push):**
`state_update` → estado completo, `launch_progress` → {status,message,progress%},
`system_log` → debug, `error` → mensaje de error, `invoke_response` → respuesta a invoke,
`cze_state_changed` → `{state: "admin"|"editing"|"closed"}` (broadcast a todos los bridges)

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

