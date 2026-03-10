# 🚀 Workspace Launcher — Guía de Testing y Desarrollo

## Requisitos Previos

- **Node.js** v18+ (incluye `npm`)
- **Windows 10/11** (los hooks y APIs nativas son exclusivos de Windows)
- **PowerShell 5.1+** (viene preinstalado en Windows 10/11)

---

## 1. Ejecución en Desarrollo

### Paso 1: Instalar dependencias

```bash
# Desde la raíz del proyecto (WorkspaceLauncher/)
cd WorkspaceLauncher
npm install

# Instalar también las dependencias del frontend
cd frontend
npm install
cd ..
```

### Paso 2: Levantar la app en modo desarrollo

```bash
# Desde WorkspaceLauncher/ — Levanta Vite + Electron simultáneamente
npm run dev
```

Esto arranca:
1. **Vite dev server** en `http://localhost:5173` (frontend React con hot-reload)
2. **Electron** que carga ese URL una vez está listo

> **Tip:** También puedes abrir solo el frontend para iterar en la UI sin Electron:
> ```bash
> cd frontend && npm run dev
> ```
> Abrirlo en el navegador en `http://localhost:5173` — usa datos mock.

---

## 2. Compilación del Ejecutable

### Opción A: Electron Packager (carpeta portátil)

```bash
# Compila el frontend y empaqueta todo como .exe portátil
npm run build
```

Resultado: `dist-exe/WorkspaceLauncher-win32-x64/WorkspaceLauncher.exe`

### Opción B: Electron Builder (portable .exe único)

```bash
npm run dist:portable
```

Resultado: `dist-builder/WorkspaceLauncher.exe` (archivo portable único)

---

## 3. Debugging y Logs

### 3.1 Consola del Frontend (DevTools)

En modo desarrollo, las DevTools se abren automáticamente.

En producción, no están habilitadas — si necesitas depurar la UI en producción, cambia temporalmente `devTools: IS_DEV` a `devTools: true` en `electron/main.js` línea 40.

**Qué buscar aquí:**
- Errores de React/JSX
- Respuestas de la API bridge (busca `[Bridge mock]` en dev mode)
- Errores de red o de carga de icons

### 3.2 Consola del Backend (Terminal)

Los logs del proceso main de Electron aparecen **directamente en la terminal** donde ejecutaste `npm run dev`.

**Prefijos importantes:**
| Prefijo | Módulo | Qué reporta |
|---------|--------|-------------|
| `[Hooks]` | hooks.js | Registro de shortcuts, eventos de mouse X1/X2 |
| `[Hooks/PS]` | hooks.js | stderr del proceso PowerShell del hook |
| `[WindowMgr]` | windowMgr.js | Errores de snap/enum de ventanas |
| `[Launcher]` | launcher.js | Errores al lanzar apps |
| `[FancyZones]` | fancyzones.js | Sincronización de layouts |

### 3.3 Debugging de Mouse Hooks

Si los botones laterales del ratón (X1/X2) no funcionan:

1. **Verificar que el proceso PowerShell está activo:**
   Busca en la terminal el mensaje:
   ```
   [Hooks] Mouse hook watcher started (PID: XXXXX)
   [Hooks/PS] HOOK_READY
   ```

2. **Verificar que los eventos llegan:**
   Presiona un botón lateral — deberías ver:
   ```
   [Hooks] Mouse event: x1
   [Hooks] → Switching desktop NEXT
   ```

3. **Si no aparece `HOOK_READY`:** El hook de bajo nivel no se instaló. Puede que otro software esté bloqueando `SetWindowsHookEx`. Cierra apps de gaming/overlay (Discord overlay, etc.).

4. **Si aparece el evento pero no cambia el escritorio:** El problema está en la simulación de teclas. Verifica que no tienes `Win+Ctrl+Arrow` mapeado a otra cosa.

### 3.4 Debugging de Selectores (Ventanas / Escritorios)

Si el selector de ventanas o escritorios aparece vacío:

1. **Verificar en la terminal** que no hay errores `[WindowMgr]`.

2. **Probar los comandos PowerShell manualmente:**
   ```powershell
   # Listar ventanas visibles
   Get-Process | Where-Object { $_.MainWindowTitle -ne '' } | Select-Object MainWindowHandle, ProcessName, MainWindowTitle

   # Listar escritorios virtuales (registro)
   Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops'
   ```

3. Si el de escritorios no devuelve nada, puede que solo tengas 1 escritorio virtual. Crea otro con `Win+Ctrl+D` y recarga.

### 3.5 Debugging del File Dialog

Si el botón "Examinar..." no abre el diálogo nativo, verifica en la terminal que no hay errores de `dialog.showOpenDialog`.

---

## 4. Estructura del Proyecto

```
WorkspaceLauncher/
├── electron/                  # Proceso main de Electron
│   ├── main.js                # Ventana, tray, registro IPC
│   ├── preload.js             # contextBridge (renderer ↔ main)
│   └── ipc/
│       ├── config.js          # CRUD config + file dialog
│       ├── hooks.js           # Keyboard/mouse hooks
│       ├── launcher.js        # Lanzamiento de apps
│       ├── windowMgr.js       # Win32 + listWindows/listDesktops
│       └── fancyzones.js      # Sync de FancyZones layouts
├── frontend/                  # Frontend React (Vite)
│   ├── src/
│   │   ├── api/bridge.js      # API bridge (Electron ↔ React)
│   │   ├── App.jsx            # Layout principal
│   │   ├── index.css          # Design system (Dark Pro theme)
│   │   └── components/        # Sidebar, AppList, ConfigPanel, TitleBar
│   └── index.html
├── package.json               # Scripts y dependencias de Electron
└── TESTING_GUIDE.md           # ← Este archivo
```

---

## 5. Flujo de Datos

```
React Component → bridge.js → preload.js (contextBridge) → ipcMain handler → PowerShell/Win32
                                                                ↓
React Component ← bridge.js ← preload.js (bridge:event) ← sendToRenderer()
```

Para las APIs de request/response (`listWindows`, `listDesktops`, `openFileDialog`):

```
React Component → bridge.invoke() → ipcRenderer.invoke() → ipcMain.handle() → PowerShell/system
              ↵ ← Promise resolve ← ───────────────────── ← return value
```
