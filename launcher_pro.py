import customtkinter as ctk
import tkinter as tk
from tkinter import filedialog, messagebox
import subprocess
import json
import os
import sys
import webbrowser
import urllib.parse
import shutil
import threading
import time
import base64
import tempfile

try:
    import win32api
    import win32gui
    import win32con
    import pygetwindow as gw
    from pyvda import AppView, get_virtual_desktops, VirtualDesktop
    import ctypes
    from ctypes import wintypes
    import ctypes.wintypes as wt
    from ctypes import windll, CFUNCTYPE, POINTER, c_int, c_uint, c_void_p
    
    # === CONSTANTES WIN32 HOOKS ===
    WH_MOUSE_LL    = 14
    WH_KEYBOARD_LL = 13
    WM_XBUTTONDOWN = 0x020B
    WM_XBUTTONUP   = 0x020C
    HC_ACTION      = 0

    XBUTTON1 = 0x0001
    XBUTTON2 = 0x0002

    VK_BROWSER_BACK    = 0xA6
    VK_BROWSER_FORWARD = 0xA7
    WM_KEYDOWN         = 0x0100
    WM_KEYUP           = 0x0101
    WM_SYSKEYDOWN      = 0x0104
    WM_SYSKEYUP        = 0x0105

    class MSLLHOOKSTRUCT(ctypes.Structure):
        _fields_ = [
            ("pt",       wt.POINT),
            ("mouseData", wt.DWORD),
            ("flags",    wt.DWORD),
            ("time",     wt.DWORD),
            ("dwExtraInfo", ctypes.POINTER(wt.ULONG)),
        ]

    class KBDLLHOOKSTRUCT(ctypes.Structure):
        _fields_ = [
            ("vkCode",      wt.DWORD),
            ("scanCode",    wt.DWORD),
            ("flags",       wt.DWORD),
            ("time",        wt.DWORD),
            ("dwExtraInfo", ctypes.POINTER(wt.ULONG)),
        ]

    HOOKPROC = CFUNCTYPE(ctypes.c_long, c_int, wt.WPARAM, wt.LPARAM)

    class GlobalHookManager:
        def __init__(self):
            self._hook_mouse    = None
            self._hook_keyboard = None
            self._mouse_cb_ref  = None
            self._kb_cb_ref     = None
            self._thread        = None
            self._thread_id     = None
            self._running       = False

            self.on_x1_down = None
            self.on_x2_down = None
            self.on_x1_up   = None
            self.on_x2_up   = None
            
            self.check_x_mapped = None # Se usará para saber si soltar o suprimir el evento con modificadores

            self._suppressed_x1 = False
            self._suppressed_x2 = False

        def _mouse_hook_proc(self, nCode, wParam, lParam):
            if nCode == HC_ACTION:
                if wParam == WM_XBUTTONDOWN or wParam == WM_XBUTTONUP:
                    data = ctypes.cast(lParam, POINTER(MSLLHOOKSTRUCT)).contents
                    button = (data.mouseData >> 16) & 0xFFFF
                    
                    real_alt = (windll.user32.GetAsyncKeyState(0x12) & 0x8000) != 0
                    real_ctrl = (windll.user32.GetAsyncKeyState(0x11) & 0x8000) != 0
                    real_shift = (windll.user32.GetAsyncKeyState(0x10) & 0x8000) != 0
                    has_modifier = real_alt or real_ctrl or real_shift
                    
                    if wParam == WM_XBUTTONDOWN:
                        if button == XBUTTON1:
                            if has_modifier and not (self.check_x_mapped and self.check_x_mapped('x1', real_alt, real_ctrl, real_shift)):
                                return windll.user32.CallNextHookEx(self._hook_mouse, nCode, wParam, lParam)
                            self._suppressed_x1 = True
                            if self.on_x1_down:
                                threading.Thread(target=self.on_x1_down, daemon=True).start()
                            return 1
                        elif button == XBUTTON2:
                            if has_modifier and not (self.check_x_mapped and self.check_x_mapped('x2', real_alt, real_ctrl, real_shift)):
                                return windll.user32.CallNextHookEx(self._hook_mouse, nCode, wParam, lParam)
                            self._suppressed_x2 = True
                            if self.on_x2_down:
                                threading.Thread(target=self.on_x2_down, daemon=True).start()
                            return 1

                    elif wParam == WM_XBUTTONUP:
                        if button == XBUTTON1 and self._suppressed_x1:
                            self._suppressed_x1 = False
                            if self.on_x1_up:
                                threading.Thread(target=self.on_x1_up, daemon=True).start()
                            return 1
                        elif button == XBUTTON2 and self._suppressed_x2:
                            self._suppressed_x2 = False
                            if self.on_x2_up:
                                threading.Thread(target=self.on_x2_up, daemon=True).start()
                            return 1

            return windll.user32.CallNextHookEx(self._hook_mouse, nCode, wParam, lParam)

        def _keyboard_hook_proc(self, nCode, wParam, lParam):
            if nCode == HC_ACTION:
                if wParam == WM_KEYDOWN or wParam == WM_KEYUP or wParam == WM_SYSKEYDOWN or wParam == WM_SYSKEYUP:
                    data = ctypes.cast(lParam, POINTER(KBDLLHOOKSTRUCT)).contents
                    if data.vkCode == VK_BROWSER_BACK or data.vkCode == VK_BROWSER_FORWARD:
                        return 1
            return windll.user32.CallNextHookEx(self._hook_keyboard, nCode, wParam, lParam)

        def _install_hooks(self):
            self._thread_id = windll.kernel32.GetCurrentThreadId()

            self._mouse_cb_ref = HOOKPROC(self._mouse_hook_proc)
            self._kb_cb_ref    = HOOKPROC(self._keyboard_hook_proc)

            self._hook_mouse = windll.user32.SetWindowsHookExW(WH_MOUSE_LL, self._mouse_cb_ref, None, 0)
            self._hook_keyboard = windll.user32.SetWindowsHookExW(WH_KEYBOARD_LL, self._kb_cb_ref, None, 0)

            if not self._hook_mouse or not self._hook_keyboard:
                print(f"[HookManager] Error instalando hooks: {ctypes.GetLastError()}")
                self._running = False
                return

            print("[HookManager] Hooks Win32 instalados correctamente (Integrado).")

            msg = wt.MSG()
            while self._running:
                bRet = windll.user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
                if bRet <= 0: break
                windll.user32.TranslateMessage(ctypes.byref(msg))
                windll.user32.DispatchMessageW(ctypes.byref(msg))

            if self._hook_mouse: windll.user32.UnhookWindowsHookEx(self._hook_mouse)
            if self._hook_keyboard: windll.user32.UnhookWindowsHookEx(self._hook_keyboard)
            print("[HookManager] Hooks desinstalados.")

        def start(self):
            if self._running: return
            self._running = True
            self._thread = threading.Thread(target=self._install_hooks, daemon=True)
            self._thread.start()

        def stop(self):
            self._running = False
            if self._thread_id:
                windll.user32.PostThreadMessageW(self._thread_id, 0x0012, 0, 0)
            if self._thread:
                self._thread.join(timeout=1)

    import sys
    WINDOWS_LIBS_AVAILABLE = True
except ImportError:
    WINDOWS_LIBS_AVAILABLE = False

try:
    import ctypes
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except:
    try:
        import ctypes
        ctypes.windll.user32.SetProcessDPIAware()
    except:
        pass

# --- CONFIGURACIÓN DE RUTAS ---
if getattr(sys, 'frozen', False):
    APP_DIR = os.path.dirname(sys.executable)
else:
    APP_DIR = os.path.dirname(os.path.abspath(__file__))

os.chdir(APP_DIR)
# -----------------------------

ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

# --- CONFIGURACIÓN DE ICONO ---
# Opción 1: Archivo externo (prioridad si existe)
ICON_NAME = "launcher_icon.ico" 
ICON_PATH = os.path.join(APP_DIR, ICON_NAME)

# Opción 2: Icono embebido (Base64). Si prefieres no depender de un archivo .ico externo,
# puedes pegar aquí el contenido base64 de tu imagen .ico.
# Para convertir tu .ico a base64 puedes usar: https://base64.guru/converter/encode/image/ico
EMBEDDED_ICON_BASE64 = "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" 

def get_effective_icon_path():
    """Retorna la ruta del archivo de icono (externo o temporal desde base64)."""
    if os.path.exists(ICON_PATH):
        return ICON_PATH
    
    if EMBEDDED_ICON_BASE64:
        try:
            temp_dir = tempfile.gettempdir()
            temp_icon = os.path.join(temp_dir, "launcher_internal_icon.ico")
            icon_data = base64.b64decode(EMBEDDED_ICON_BASE64)
            with open(temp_icon, "wb") as f:
                f.write(icon_data)
            return temp_icon
        except Exception as e:
            print(f"Error procesando icono embebido: {e}")
    return None

def set_win_taskbar_icon():
    """Asegura que Windows trate al script como una app independiente para mostrar su propio icono."""
    if sys.platform == "win32":
        try:
            import ctypes
            myappid = 'davidesteve.workspace.launcher.pro' # ID único arbitrario
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
        except Exception as e:
            print(f"Error configurando ID de tarea: {e}")

set_win_taskbar_icon()
# ------------------------------

# SEPARADOR MÁGICO PARA LAS PESTAÑAS
TAB_SEPARATOR = "--- NUEVA PESTAÑA ---"


class AddIDEDialog(ctk.CTkToplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Añadir Proyecto")
        self.geometry("450x200")
        self.result = None
        self.transient(parent)
        self.grab_set()

        # Seleccionar ruta
        self.path_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.path_frame.pack(fill="x", padx=20, pady=(20, 10))
        
        ctk.CTkLabel(self.path_frame, text="Ruta:", width=50).pack(side="left", padx=(0, 10))
        self.path_label = ctk.CTkLabel(self.path_frame, text="No seleccionada", fg_color="#333", corner_radius=5)
        self.path_label.pack(side="left", fill="x", expand=True, padx=(0, 10))
        ctk.CTkButton(self.path_frame, text="📂", width=40, command=self.browse).pack(side="left")

        # Seleccionar Comando
        self.cmd_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.cmd_frame.pack(fill="x", padx=20, pady=10)
        ctk.CTkLabel(self.cmd_frame, text="IDE Cmd:", width=60).pack(side="left", padx=(0, 10))
        self.ide_var = ctk.StringVar(value="code")
        self.ide_entry = ctk.CTkEntry(self.cmd_frame, textvariable=self.ide_var)
        self.ide_entry.pack(side="left", fill="x", expand=True)

        # Botones
        self.btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=(20, 10))
        ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#555", command=self.destroy, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.btn_frame, text="Siguiente >", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=100).pack(side="right")

        self.path_value = ""

    def browse(self):
        p = filedialog.askdirectory(title="Seleccionar proyecto")
        if p:
            self.path_label.configure(text=p)
            self.path_value = p

    def save(self):
        if not self.path_value:
            messagebox.showwarning("Aviso", "Selecciona una ruta primero")
            return
        cmd = self.ide_var.get().strip()
        if not cmd:
            messagebox.showwarning("Aviso", "Introduce un comando IDE")
            return
            
        self.result = {"path": os.path.normpath(self.path_value), "ide_cmd": cmd}
        self.destroy()

class AddMultiWebDialog(ctk.CTkToplevel):
    # Navegadores conocidos y sus posibles rutas en el registro
    KNOWN_BROWSERS = {
        "Microsoft Edge": {"cmd": "msedge", "reg_names": ["Microsoft Edge"]},
        "Google Chrome": {"cmd": "chrome", "reg_names": ["Google Chrome"]},
        "Firefox": {"cmd": "firefox", "reg_names": ["Firefox"]},
        "Brave": {"cmd": "brave", "reg_names": ["Brave"]},
        "Opera": {"cmd": "opera", "reg_names": ["Opera Stable", "Opera"]},
        "Vivaldi": {"cmd": "vivaldi", "reg_names": ["Vivaldi"]},
    }

    def __init__(self, parent, existing_browser=None):
        super().__init__(parent)
        self.title("Añadir Multi-Web")
        self.geometry("550x500")
        self.result = None
        self.transient(parent)
        self.grab_set()
        
        # --- Selector de Navegador ---
        browser_frame = ctk.CTkFrame(self, fg_color="transparent")
        browser_frame.pack(fill="x", padx=20, pady=(15, 5))
        
        ctk.CTkLabel(browser_frame, text="🌐 Navegador:", font=("Roboto", 14, "bold")).pack(side="left", padx=(0, 10))
        
        self.detected_browsers = self._detect_browsers()
        browser_options = ["🖥️ Por defecto del sistema"] + self.detected_browsers + ["✏️ Comando personalizado..."]
        
        self.browser_var = ctk.StringVar(value=existing_browser if existing_browser and existing_browser in browser_options else "🖥️ Por defecto del sistema")
        self.browser_combo = ctk.CTkComboBox(browser_frame, values=browser_options, variable=self.browser_var, 
                                              width=280, command=self._on_browser_change)
        self.browser_combo.pack(side="left", fill="x", expand=True)
        
        # Campo de comando personalizado (oculto por defecto)
        self.custom_cmd_frame = ctk.CTkFrame(self, fg_color="transparent")
        ctk.CTkLabel(self.custom_cmd_frame, text="Comando:", width=70).pack(side="left", padx=(0, 5))
        self.custom_cmd_var = ctk.StringVar(value="")
        self.custom_cmd_entry = ctk.CTkEntry(self.custom_cmd_frame, textvariable=self.custom_cmd_var, 
                                              placeholder_text="ej: C:\\Program Files\\MiNavegador\\browser.exe")
        self.custom_cmd_entry.pack(side="left", fill="x", expand=True, padx=5)
        # Solo visible si se elige personalizado
        if existing_browser == "✏️ Comando personalizado...":
            self.custom_cmd_frame.pack(fill="x", padx=20, pady=(0, 5))
        
        # --- URLs ---
        ctk.CTkLabel(self, text="URLs para este grupo (Multi-pestaña):", font=("Roboto", 14, "bold")).pack(anchor="w", padx=20, pady=(10, 10))
        
        self.tabs_scroll = ctk.CTkScrollableFrame(self, height=200)
        self.tabs_scroll.pack(fill="both", expand=True, padx=20, pady=5)
        
        ctk.CTkButton(self, text="➕ Añadir URL", command=self.add_tab_entry, fg_color="#4B4B4B", hover_color="#333").pack(pady=10)
        
        self.tab_entries = []
        self.add_tab_entry("https://google.com")
        
        self.btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=(10, 15))
        ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#555", command=self.destroy, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.btn_frame, text="Siguiente >", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=140).pack(side="right")

    def _detect_browsers(self):
        """Detecta navegadores instalados leyendo el registro de Windows."""
        found = []
        try:
            import winreg
            reg_path = r"SOFTWARE\Clients\StartMenuInternet"
            for hive in [winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER]:
                try:
                    key = winreg.OpenKey(hive, reg_path)
                    i = 0
                    while True:
                        try:
                            subkey_name = winreg.EnumKey(key, i)
                            try:
                                sk = winreg.OpenKey(key, subkey_name)
                                display_name, _ = winreg.QueryValueEx(sk, "")
                                winreg.CloseKey(sk)
                            except:
                                display_name = subkey_name
                            
                            # Intentar obtener el ejecutable
                            try:
                                cmd_key = winreg.OpenKey(key, f"{subkey_name}\\shell\\open\\command")
                                cmd_val, _ = winreg.QueryValueEx(cmd_key, "")
                                winreg.CloseKey(cmd_key)
                            except:
                                cmd_val = ""
                            
                            if display_name and display_name not in found:
                                found.append(display_name)
                            i += 1
                        except OSError:
                            break
                    winreg.CloseKey(key)
                except OSError:
                    continue
        except:
            # Fallback: intentar detectar por ejecutable en PATH
            import shutil
            for name, info in self.KNOWN_BROWSERS.items():
                if shutil.which(info["cmd"]):
                    if name not in found:
                        found.append(name)
        return found if found else ["Microsoft Edge", "Google Chrome", "Firefox"]
    
    def _on_browser_change(self, choice):
        if choice == "✏️ Comando personalizado...":
            self.custom_cmd_frame.pack(fill="x", padx=20, pady=(0, 5), after=self.browser_combo.master)
        else:
            self.custom_cmd_frame.pack_forget()

    def _get_browser_command(self):
        """Devuelve el comando del navegador seleccionado."""
        choice = self.browser_var.get()
        if choice == "🖥️ Por defecto del sistema":
            return "default"
        elif choice == "✏️ Comando personalizado...":
            cmd = self.custom_cmd_var.get().strip()
            return cmd if cmd else "default"
        else:
            # Buscar en la tabla de conocidos
            for name, info in self.KNOWN_BROWSERS.items():
                if name in choice or choice in name:
                    return info["cmd"]
            # Si no está en conocidos, intentar extraer del registro
            try:
                import winreg
                for hive in [winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER]:
                    try:
                        cmd_key = winreg.OpenKey(hive, f"SOFTWARE\\Clients\\StartMenuInternet\\{choice}\\shell\\open\\command")
                        cmd_val, _ = winreg.QueryValueEx(cmd_key, "")
                        winreg.CloseKey(cmd_key)
                        # Limpiar comillas del path
                        return cmd_val.strip('"')
                    except: continue
            except: pass
            return choice.lower().replace(" ", "")

    def add_tab_entry(self, text=""):
        idx = len(self.tab_entries) + 1
        frame = ctk.CTkFrame(self.tabs_scroll)
        frame.pack(fill="x", pady=5, padx=5)
        
        lbl = ctk.CTkLabel(frame, text=f"URL {idx}:", width=50)
        lbl.pack(side="left", padx=5)
        
        entry = ctk.CTkEntry(frame)
        entry.pack(side="left", fill="x", expand=True, padx=5)
        entry.insert(0, text)
        
        btn = ctk.CTkButton(frame, text="✖", width=30, fg_color="#AA0000", hover_color="#770000", 
                            command=lambda f=frame, e=entry: self.remove_tab_entry(f, e))
        btn.pack(side="right", padx=5)
        
        self.tab_entries.append(entry)

    def remove_tab_entry(self, frame, entry):
        if entry in self.tab_entries:
            self.tab_entries.remove(entry)
        frame.destroy()

    def save(self):
        urls = []
        for e in self.tab_entries:
            u = e.get().strip()
            if u:
                if not u.startswith("http"):
                    u = "https://" + u
                urls.append(u)
        
        if not urls:
            messagebox.showwarning("Aviso", "Introduce al menos una URL.")
            return
            
        self.result = {
            "path": urls[0],
            "cmd": f" {TAB_SEPARATOR} ".join(urls),
            "browser": self._get_browser_command(),
            "browser_display": self.browser_var.get()
        }
        self.destroy()

class AssignLayoutsDialog(ctk.CTkToplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Asignar Layouts por Monitor/Escritorio")
        self.geometry("650x550")
        self.transient(parent)
        self.grab_set()

        self.parent_app = parent
        if hasattr(parent, "load_fancyzones_layouts"):
            parent.load_fancyzones_layouts()
            
        self.applied_path = os.path.join(parent.fancyzones_path, "applied-layouts.json")
        self.applied_data = None
        self.combos_map = []  # Lista de tuplas (diccionario_referencia, variable_tkinter)

        ctk.CTkLabel(self, text="Distribuciones aplicadas actualmente a tus pantallas:", font=("Roboto", 14, "bold")).pack(pady=(15, 10), padx=20, anchor="w")

        # Layout mapping Name -> UUID y UUID -> Name
        self.name_to_uuid = {}
        self.uuid_to_name = {"{00000000-0000-0000-0000-000000000000}": "Default Priority Grid"}
        
        for lname, linfo in parent.available_layouts.items():
            u = linfo.get("uuid", "")
            if u:
                self.name_to_uuid[lname] = u
                self.uuid_to_name[u] = lname

        self.scroll = ctk.CTkScrollableFrame(self, height=350, fg_color="#2B2B2B")
        self.scroll.pack(fill="both", expand=True, padx=20, pady=5)

        self.load_data()

        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=20, pady=15)
        
        ctk.CTkButton(btn_frame, text="Cancelar", fg_color="#555", command=self.destroy, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(btn_frame, text="Guardar en PowerToys", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=180).pack(side="right")
        ctk.CTkButton(btn_frame, text="🔄 Recargar", fg_color="#007ACC", hover_color="#005A9E", command=self.refresh_data, width=120).pack(side="left")

    def get_active_fz_monitors(self):
        active_ids = []
        if not WINDOWS_LIBS_AVAILABLE: return active_ids
        try:
            import win32api
            i = 0
            while True:
                d = win32api.EnumDisplayDevices(None, i, 0)
                if not d.DeviceName: break
                if d.StateFlags & 1:
                    m_i = 0
                    while True:
                        try:
                            m = win32api.EnumDisplayDevices(d.DeviceName, m_i, 0)
                            if not m.DeviceID: break
                            parts = m.DeviceID.split("\\")
                            if len(parts) > 1 and parts[0] == "MONITOR":
                                active_ids.append(parts[1])
                            m_i += 1
                        except: break
                i += 1
        except Exception: pass
        return active_ids

    def refresh_data(self):
        if hasattr(self.parent_app, "load_fancyzones_layouts"):
            self.parent_app.load_fancyzones_layouts()
        for w in self.scroll.winfo_children():
            w.destroy()
        self.combos_map.clear()
        self.load_data()

    def _create_row(self, item):
        row = ctk.CTkFrame(self.scroll, fg_color="#333", corner_radius=5)
        row.pack(fill="x", pady=4, padx=5)
        lbl = ctk.CTkLabel(row, text=item["text"], anchor="w", font=("Roboto", 13))
        lbl.pack(side="left", padx=10, fill="x", expand=True)
        combo = ctk.CTkComboBox(row, values=item["opts"], variable=item["var"], width=180)
        combo.pack(side="right", padx=10, pady=5)
        self.combos_map.append((item["app_lay_ref"], item["var"]))

    def load_data(self):
        if not os.path.exists(self.applied_path):
            ctk.CTkLabel(self.scroll, text="No se encontró applied-layouts.json").pack(pady=20)
            return
            
        try:
            with open(self.applied_path, 'r', encoding='utf-8') as f:
                self.applied_data = json.load(f)
                
            layouts_list = self.applied_data.get("applied-layouts", [])
            
            desk_names_map = {}
            active_vd_guids = []
            if WINDOWS_LIBS_AVAILABLE:
                try:
                    for i, d in enumerate(get_virtual_desktops()):
                        g = str(d.id).upper()
                        if not g.startswith("{"): g = "{" + g + "}"
                        desk_names_map[g] = d.name if d.name else f"Escritorio {i+1}"
                        active_vd_guids.append(g)
                except: pass

            active_fz_mons = self.get_active_fz_monitors()
            
            known_monitor_devices = {}
            existing_combos = set()
            for al in layouts_list:
                dev = al.get("device", {})
                mon_str = dev.get("monitor", "")
                if mon_str:
                    known_monitor_devices[mon_str] = {
                        "monitor": mon_str,
                        "monitor-instance": dev.get("monitor-instance", ""),
                        "monitor-number": dev.get("monitor-number", 1),
                        "serial-number": dev.get("serial-number", "0")
                    }
                vd_guid = dev.get("virtual-desktop", "").upper()
                if not vd_guid.startswith("{"): vd_guid = "{" + vd_guid + "}"
                existing_combos.add((mon_str, vd_guid))

            for mon_id, dev_base in known_monitor_devices.items():
                is_active_mon = (mon_id in active_fz_mons) or ("LOCALDISPLAY" in mon_id)
                if is_active_mon:
                    for g in active_vd_guids:
                        if (mon_id, g) not in existing_combos:
                            new_entry = {
                                "device": {
                                    "monitor": mon_id,
                                    "monitor-instance": dev_base.get("monitor-instance", ""),
                                    "monitor-number": dev_base.get("monitor-number", 1),
                                    "serial-number": dev_base.get("serial-number", "0"),
                                    "virtual-desktop": g
                                },
                                "applied-layout": {
                                    "uuid": "{00000000-0000-0000-0000-000000000000}",
                                    "type": "priority-grid",
                                    "show-spacing": True,
                                    "spacing": 16,
                                    "zone-count": 0,
                                    "sensitivity-radius": 20
                                }
                            }
                            layouts_list.append(new_entry)
                            existing_combos.add((mon_id, g))

            active_ui = []
            inactive_ui = []

            for al in layouts_list:
                dev = al.get("device", {})
                mon_str = dev.get("monitor", "Unk")
                mon_num = dev.get("monitor-number", "?")
                vd_guid = dev.get("virtual-desktop", "?").upper()
                if not vd_guid.startswith("{"): vd_guid = "{" + vd_guid + "}"
                
                clean_mon = mon_str.replace("\\\\.\\", "").replace("DISPLAY", "Display ")
                if clean_mon == mon_str and "LOCALDISPLAY" in mon_str: clean_mon = "Display Principal"
                
                vd_name = desk_names_map.get(vd_guid)
                is_vd_active = bool(vd_name)
                if not vd_name: vd_name = f"Virtual D. ({vd_guid[:8]})"
                
                is_mon_active = False
                for am in active_fz_mons:
                    if am in mon_str: is_mon_active = True
                if "LOCALDISPLAY" in mon_str: is_mon_active = True

                is_active = is_vd_active and is_mon_active
                
                app_lay = al.get("applied-layout", {})
                curr_uuid = app_lay.get("uuid", "")
                curr_name = self.uuid_to_name.get(curr_uuid.upper(), self.uuid_to_name.get(curr_uuid, "Desconocido/Priority Grid"))
                
                available_opts = list(self.name_to_uuid.keys())
                if curr_name not in available_opts and curr_name != "Desconocido/Priority Grid":
                    available_opts.append(curr_name)
                    
                var = ctk.StringVar(value=curr_name if curr_name in available_opts else (available_opts[0] if available_opts else ""))
                
                item_data = {
                    "text": f"📺 Pantalla {mon_num} [{clean_mon}]  |  🖥️ {vd_name}",
                    "var": var,
                    "opts": available_opts,
                    "app_lay_ref": app_lay
                }
                
                if is_active: active_ui.append(item_data)
                else: inactive_ui.append(item_data)

            active_ui.sort(key=lambda x: x["text"])
            inactive_ui.sort(key=lambda x: x["text"])

            if active_ui:
                ctk.CTkLabel(self.scroll, text="🟢 ACTIVOS (Conectados ahora)", font=("Roboto", 13, "bold"), text_color="#2CC985").pack(anchor="w", pady=(5, 2), padx=10)
                for item in active_ui:
                    self._create_row(item)
            
            if inactive_ui:
                ctk.CTkLabel(self.scroll, text="⚪ INACTIVOS / HISTORIAL", font=("Roboto", 13, "bold"), text_color="#888888").pack(anchor="w", pady=(15, 2), padx=10)
                for item in inactive_ui:
                    self._create_row(item)
                
        except Exception as e:
            import traceback
            traceback.print_exc()
            ctk.CTkLabel(self.scroll, text=f"Error leyendo config: {e}").pack(pady=20)

    def save(self):
        if not self.applied_data:
            self.destroy()
            return
            
        try:
            for app_lay_ref, var in self.combos_map:
                selected_name = var.get()
                if selected_name in self.name_to_uuid:
                    app_lay_ref["uuid"] = self.name_to_uuid[selected_name]
                    app_lay_ref["type"] = "custom"
            
            with open(self.applied_path, 'w', encoding='utf-8') as f:
                json.dump(self.applied_data, f, indent=4)
                
            messagebox.showinfo("Éxito", "Configuración de monitores guardada.\nEs posible que tengas que reiniciar FancyZones (o mover una ventana con Shift) para activar los cambios visualmente en FZ.", parent=self)
            self.destroy()
        except Exception as e:
            messagebox.showerror("Error", f"No se pudo guardar:\n{e}", parent=self)

class AdvancedItemDialog(ctk.CTkToplevel):
    def __init__(self, parent, title="Configurar Item", path_or_url="", item_type="exe", item_data=None):
        super().__init__(parent)
        self.title(title)
        self.parent_app = parent
        self.item_type = item_type
        self.item_data = item_data or {}
        
        # -- Refrescar layouts para mantener los datos actualizados --
        if hasattr(parent, "load_fancyzones_layouts"):
            parent.load_fancyzones_layouts()

        
        if self.item_type in ["powershell", "url"]:
            self.geometry("850x550")
        else:
            self.geometry("450x550")
            
        self.result = None
        self.transient(parent)
        self.grab_set()

        self.grid_rowconfigure(0, weight=1)
        self.grid_columnconfigure(0, weight=1)

        self.left_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.left_frame.grid(row=0, column=0, sticky="nsew", padx=0, pady=0)

        # Ruta
        ctk.CTkLabel(self.left_frame, text="Ruta / URL:").pack(pady=(15, 0), padx=20, anchor="w")
        
        self.path_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.path_frame.pack(fill="x", padx=20, pady=5)
        
        self.path_var = ctk.StringVar(value=path_or_url)
        self.path_entry = ctk.CTkEntry(self.path_frame, textvariable=self.path_var)
        self.path_entry.pack(side="left", fill="x", expand=True)
        
        if self.item_type in ["exe", "vscode", "ide", "obsidian", "powershell"]:
            ctk.CTkButton(self.path_frame, text="📂", width=30, command=self.browse_path).pack(side="left", padx=(5, 0))

        if self.item_type == "ide":
            self.ide_cmd_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
            self.ide_cmd_frame.pack(fill="x", padx=20, pady=5)
            ctk.CTkLabel(self.ide_cmd_frame, text="IDE Cmd:", width=60).pack(side="left")
            self.ide_cmd_var = ctk.StringVar(value=self.item_data.get("ide_cmd", "code"))
            self.ide_cmd_entry = ctk.CTkEntry(self.ide_cmd_frame, textvariable=self.ide_cmd_var)
            self.ide_cmd_entry.pack(side="left", fill="x", expand=True, padx=5)

        # Monitor
        monitors = parent.available_monitors if hasattr(parent, 'available_monitors') and parent.available_monitors else ["Por defecto"]
        if WINDOWS_LIBS_AVAILABLE and len(monitors) == 1:
            try:
                for i, m in enumerate(win32api.EnumDisplayMonitors()):
                    monitors.append(f"Monitor {i+1}")
            except: pass

        self.mon_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.mon_frame.pack(fill="x", padx=20, pady=5)
        ctk.CTkLabel(self.mon_frame, text="Pantalla:", width=80).pack(side="left")
        
        mon_val = self.item_data.get("monitor", monitors[0])
        self.monitor_var = ctk.StringVar(value=mon_val)
        self.monitor_combo = ctk.CTkComboBox(self.mon_frame, values=monitors, variable=self.monitor_var, command=self.on_desktop_change)
        self.monitor_combo.pack(side="left", fill="x", expand=True, padx=5)

        # Desktop
        desktops = ["Por defecto"]
        if WINDOWS_LIBS_AVAILABLE:
            try:
                num_desktops = len(get_virtual_desktops())
                for i in range(1, num_desktops + 1):
                    desktops.append(f"Escritorio {i}")
            except: pass

        self.desk_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.desk_frame.pack(fill="x", padx=20, pady=5)
        ctk.CTkLabel(self.desk_frame, text="Escritorio:", width=80).pack(side="left")
        
        desk_val = self.item_data.get("desktop", desktops[0])
        self.desktop_var = ctk.StringVar(value=desk_val)
        self.desktop_combo = ctk.CTkComboBox(self.desk_frame, values=desktops, variable=self.desktop_var, command=self.on_desktop_change)
        self.desktop_combo.pack(side="left", fill="x", expand=True, padx=5)

        # Retardo
        self.delay_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.delay_frame.pack(fill="x", padx=20, pady=5)
        ctk.CTkLabel(self.delay_frame, text="Retardo (s):", width=80).pack(side="left")
        self.delay_var = ctk.StringVar(value=self.item_data.get("delay", "0"))
        self.delay_entry = ctk.CTkEntry(self.delay_frame, textvariable=self.delay_var, width=50)
        self.delay_entry.pack(side="left", padx=5)

        # ======= FANCYZONES FASE ============
        ctk.CTkLabel(self.left_frame, text="Zona FancyZones [Clic en el panel]:", font=("Roboto", 12, "bold")).pack(pady=(15,0), padx=20, anchor="w")
        
        self.fz_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.fz_frame.pack(fill="x", padx=20, pady=5)
        ctk.CTkLabel(self.fz_frame, text="Layout Base:", width=80).pack(side="left")

        layouts = ["Ninguno"] + list(parent.available_layouts.keys()) if hasattr(parent, 'available_layouts') and parent.available_layouts else ["Ninguno"]
        
        # Cargar selección inicial de layout
        init_layout = layouts[0]
        self.selected_zone_str = "Ninguna"
        saved_fz = self.item_data.get("fancyzone", "Ninguna")
        
        if saved_fz != "Ninguna":
            parts = saved_fz.rsplit(" - Zona ", 1)
            if len(parts) == 2 and parts[0] in layouts:
                init_layout = parts[0]
                self.selected_zone_str = saved_fz
        elif hasattr(parent, 'default_layout_name') and parent.default_layout_name in layouts:
            init_layout = parent.default_layout_name
            
        self.layout_var = ctk.StringVar(value=init_layout)
        self.layout_combo = ctk.CTkComboBox(self.fz_frame, values=layouts, variable=self.layout_var, command=self.update_preview)
        self.layout_combo.pack(side="left", fill="x", expand=True, padx=5)

        # Preview frame superior
        self.preview_lbl = ctk.CTkLabel(self.left_frame, text=f"Actual: {self.selected_zone_str}", text_color="#2CC985")
        self.preview_lbl.pack(padx=20, pady=(2, 5), anchor="w")

        self.preview_container = ctk.CTkFrame(self.left_frame, height=180, fg_color="#222", corner_radius=10)
        self.preview_container.pack(fill="x", padx=20, pady=5)
        self.preview_container.pack_propagate(False)

        # Botones inferiores
        self.btn_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=(15, 20))
        ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#555", command=self.cancel, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.btn_frame, text="Guardar", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=100).pack(side="right")

        # Dibujar preview inicial
        self.after(100, self.update_preview)

        # ======= TABS FASE (POWERSHELL / URL) ============
        if self.item_type in ["powershell", "url"]:
            self.grid_columnconfigure(1, weight=1)
            self.right_frame = ctk.CTkFrame(self, fg_color="transparent")
            self.right_frame.grid(row=0, column=1, sticky="nsew", padx=10, pady=15)
            
            title_text = "Pestañas de la Terminal:" if self.item_type == "powershell" else "URLs a abrir (Multi-pestaña):"
            ctk.CTkLabel(self.right_frame, text=title_text, font=("Roboto", 14, "bold")).pack(anchor="w", pady=(0, 10))
            
            self.tabs_scroll = ctk.CTkScrollableFrame(self.right_frame, height=400)
            self.tabs_scroll.pack(fill="both", expand=True, pady=5)
            
            btn_text = "➕ Añadir Pestaña" if self.item_type == "powershell" else "➕ Añadir URL"
            ctk.CTkButton(self.right_frame, text=btn_text, command=self.add_tab_entry, fg_color="#4B4B4B", hover_color="#333").pack(pady=10)
            
            # Selector de navegador para URLs
            if self.item_type == "url":
                browser_section = ctk.CTkFrame(self.right_frame, fg_color="transparent")
                browser_section.pack(fill="x", pady=(5, 0))
                ctk.CTkLabel(browser_section, text="🌐 Navegador:", font=("Roboto", 13, "bold")).pack(side="left", padx=(0, 8))
                
                detected = AddMultiWebDialog._detect_browsers(AddMultiWebDialog)
                browser_options = ["🖥️ Por defecto del sistema"] + detected + ["✏️ Comando personalizado..."]
                
                saved_browser = self.item_data.get('browser_display', '🖥️ Por defecto del sistema')
                self.browser_var = ctk.StringVar(value=saved_browser if saved_browser in browser_options else '🖥️ Por defecto del sistema')
                self.browser_combo = ctk.CTkComboBox(browser_section, values=browser_options, variable=self.browser_var,
                                                     width=250, command=self._on_browser_change)
                self.browser_combo.pack(side="left", fill="x", expand=True)
                
                self.custom_browser_frame = ctk.CTkFrame(self.right_frame, fg_color="transparent")
                ctk.CTkLabel(self.custom_browser_frame, text="Cmd:", width=40).pack(side="left")
                self.custom_browser_var = ctk.StringVar(value=self.item_data.get('browser', '') if saved_browser.startswith('✏️') else '')
                ctk.CTkEntry(self.custom_browser_frame, textvariable=self.custom_browser_var,
                             placeholder_text="C:\\...\\browser.exe").pack(side="left", fill="x", expand=True, padx=5)
                if saved_browser.startswith('✏️'):
                    self.custom_browser_frame.pack(fill="x", pady=(2, 0))
            
            self.tab_entries = []
            
            existing_cmd = self.item_data.get("cmd", "")
            if existing_cmd:
                tabs = existing_cmd.split(TAB_SEPARATOR)
                for t in tabs:
                    clean_t = t.strip()
                    self.add_tab_entry(clean_t)
            elif path_or_url and self.item_type == "url":
                self.add_tab_entry(path_or_url)
            else:
                self.add_tab_entry()

    def browse_path(self):
        if self.item_type == "exe":
            if p := filedialog.askopenfilename(filetypes=[("Exe", "*.exe")]):
                self.path_var.set(os.path.normpath(p))
        else:
            if p := filedialog.askdirectory():
                self.path_var.set(os.path.normpath(p))

    def on_desktop_change(self, choice):
        if not WINDOWS_LIBS_AVAILABLE: return
            
        desk_choice = self.desktop_var.get()
        mon_choice = self.monitor_var.get()
        
        try:
            if desk_choice.startswith("Escritorio "):
                idx = int(desk_choice.replace("Escritorio ", "")) - 1
            else:
                return

            vds = get_virtual_desktops()
            if 0 <= idx < len(vds):
                target_guid = str(vds[idx].id).upper()
                if not target_guid.startswith("{"): target_guid = "{" + target_guid + "}"
                
                # Intentar auto-asignar dependiendo del monitor amigable
                assigned_layout_name = self.parent_app.applied_mappings.get(f"{target_guid}_{mon_choice}")
                
                # Fallback genérico si no lo encontró exacto
                if not assigned_layout_name:
                    for k, v in self.parent_app.applied_mappings.items():
                        if k.startswith(target_guid):
                            assigned_layout_name = v
                            break
                            
                if assigned_layout_name and assigned_layout_name in self.layout_combo.cget("values"):
                    self.layout_var.set(assigned_layout_name)
                    self.selected_zone_str = "Ninguna"
                    self.update_preview()
        except:
            pass

    def add_tab_entry(self, text=""):
        idx = len(self.tab_entries) + 1
        frame = ctk.CTkFrame(self.tabs_scroll)
        frame.pack(fill="x", pady=5, padx=5)
        
        lbl_text = f"Tab {idx}:" if self.item_type == "powershell" else f"URL {idx}:"
        lbl = ctk.CTkLabel(frame, text=lbl_text, width=50)
        lbl.pack(side="left", padx=5)
        
        entry = ctk.CTkEntry(frame)
        entry.pack(side="left", fill="x", expand=True, padx=5)
        entry.insert(0, text)
        
        btn = ctk.CTkButton(frame, text="✖", width=30, fg_color="#AA0000", hover_color="#770000", 
                            command=lambda f=frame, e=entry: self.remove_tab_entry(f, e))
        btn.pack(side="right", padx=5)
        
        self.tab_entries.append(entry)

    def remove_tab_entry(self, frame, entry):
        if entry in self.tab_entries:
            self.tab_entries.remove(entry)
        frame.destroy()

    def update_preview(self, *_):
        for w in self.preview_container.winfo_children(): w.destroy()
        
        lname = self.layout_var.get()
        if lname == "Ninguno" or lname not in self.parent_app.available_layouts:
            ctk.CTkLabel(self.preview_container, text="Sin visualización", text_color="gray").pack(expand=True)
            self.selected_zone_str = "Ninguna"
            self.preview_lbl.configure(text=f"Actual: {self.selected_zone_str}", text_color="gray")
            return
            
        layout_info = self.parent_app.available_layouts[lname]
        ltype = layout_info.get("type", "")
        
        active_color = "#005A9E"
        hover_col = "#007ACC"
        
        if ltype == "canvas":
            zones = layout_info.get("zones", [])
            max_w, max_h = 1, 1
            for z in zones:
                w, h = z.get("width", 100), z.get("height", 100)
                if w > max_w: max_w = w
                if h > max_h: max_h = h
                
            for i, z in enumerate(zones):
                rel_x = z.get("X",0) / max_w
                rel_y = z.get("Y",0) / max_h
                rel_w = z.get("width",10) / max_w
                rel_h = z.get("height",10) / max_h
                
                btn = ctk.CTkButton(self.preview_container, text=f"Z {i+1}", fg_color=active_color, hover_color=hover_col,
                                    corner_radius=2, command=lambda idx=i: self.select_zone(lname, idx))
                if rel_x + rel_w > 1: rel_w = 1 - rel_x
                if rel_y + rel_h > 1: rel_h = 1 - rel_y
                btn.place(relx=rel_x, rely=rel_y, relwidth=rel_w, relheight=rel_h)

        elif ltype == "grid":
            rows_perc = layout_info.get("rows-percentage", [10000])
            cols_perc = layout_info.get("columns-percentage", [10000])
            cell_map = layout_info.get("cell-child-map", [[0]])
            
            for r, perc in enumerate(rows_perc):
                self.preview_container.grid_rowconfigure(r, weight=perc)
            for c, perc in enumerate(cols_perc):
                self.preview_container.grid_columnconfigure(c, weight=perc)
                
            painted = set()
            for r_i, row in enumerate(cell_map):
                for c_i, z_idx in enumerate(row):
                    if z_idx not in painted:
                        painted.add(z_idx)
                        
                        min_r, max_r, min_c, max_c = r_i, r_i, c_i, c_i
                        for check_r, r_row in enumerate(cell_map):
                            for check_c, check_val in enumerate(r_row):
                                if check_val == z_idx:
                                    min_r, max_r = min(min_r, check_r), max(max_r, check_r)
                                    min_c, max_c = min(min_c, check_c), max(max_c, check_c)
                                    
                        rspan = max_r - min_r + 1
                        cspan = max_c - min_c + 1
                        
                        btn = ctk.CTkButton(self.preview_container, text=f"{z_idx+1}", 
                                            fg_color=active_color, hover_color=hover_col, 
                                            border_width=2, border_color="#181818", corner_radius=0,
                                            command=lambda idx=z_idx, b=None: self.select_zone(lname, idx))
                        btn.grid(row=min_r, column=min_c, rowspan=rspan, columnspan=cspan, sticky="nsew")
                        
    def select_zone(self, layout_name, zone_idx):
        self.selected_zone_str = f"{layout_name} - Zona {zone_idx+1}"
        
        # Guardar el UUID del layout seleccionado
        self.selected_layout_uuid = ""
        if hasattr(self.parent_app, 'available_layouts') and layout_name in self.parent_app.available_layouts:
            self.selected_layout_uuid = self.parent_app.available_layouts[layout_name].get("uuid", "")
            
        self.preview_lbl.configure(text=f"Actual: {self.selected_zone_str}", text_color="#2CC985")

    def save(self):
        p = self.path_var.get().strip()
        if self.item_type == "url":
            pass # Para web el path lo saca de la primera pestaña luego
        elif not p:
            messagebox.showwarning("Aviso", "La ruta no puede estar vacía.")
            return

        self.result = {
            "path": p,
            "monitor": self.monitor_var.get(),
            "desktop": self.desktop_var.get(),
            "fancyzone": self.selected_zone_str,
            "fancyzone_uuid": getattr(self, "selected_layout_uuid", ""),
            "delay": self.delay_var.get()
        }
        
        if self.item_type == "ide":
            cmd = self.ide_cmd_var.get().strip()
            if cmd: self.result["ide_cmd"] = cmd
            else:
                messagebox.showwarning("Aviso", "Introduce un comando IDE")
                return
        if self.item_type in ["powershell", "url"]:
            tabs_texts = []
            for e in self.tab_entries:
                txt = e.get().strip()
                if txt:
                    if self.item_type == "url" and not txt.startswith("http"):
                        txt = "https://" + txt
                    tabs_texts.append(txt)
            if not tabs_texts:
                tabs_texts.append("")
            self.result["cmd"] = f" {TAB_SEPARATOR} ".join(tabs_texts)
            if self.item_type == "url":
                self.result["path"] = tabs_texts[0] if tabs_texts else ""
                # Guardar navegador
                if hasattr(self, 'browser_var'):
                    display = self.browser_var.get()
                    self.result["browser_display"] = display
                    if display == "🖥️ Por defecto del sistema":
                        self.result["browser"] = "default"
                    elif display == "✏️ Comando personalizado...":
                        self.result["browser"] = self.custom_browser_var.get().strip() or "default"
                    else:
                        # Buscar en la tabla de conocidos
                        cmd = display.lower().replace(" ", "")
                        for name, info in AddMultiWebDialog.KNOWN_BROWSERS.items():
                            if name in display or display in name:
                                cmd = info["cmd"]
                                break
                        self.result["browser"] = cmd
            
        self.destroy()

    def cancel(self):
        self.destroy()

    def _on_browser_change(self, choice):
        if choice == "✏️ Comando personalizado...":
            self.custom_browser_frame.pack(fill="x", pady=(2, 0))
        else:
            self.custom_browser_frame.pack_forget()

class CleanWorkspaceDialog(ctk.CTkToplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Limpiar Entorno - Cerrar Ventanas")
        self.geometry("750x650")
        self.transient(parent)
        self.grab_set()
        self.parent_app = parent

        # Top Frame para Quick Actions
        self.top_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.top_frame.pack(fill="x", padx=20, pady=(15, 5))

        ctk.CTkLabel(self.top_frame, text="Selección rápida:", font=("Roboto", 14, "bold")).pack(side="left", padx=(0, 10))
        
        ctk.CTkButton(self.top_frame, text="Config. Actual", width=110, command=self.select_config_only).pack(side="left", padx=5)
        ctk.CTkButton(self.top_frame, text="🚀 Solo Lanzadas", width=120, fg_color="#2CC985", hover_color="#24A36B", command=self.select_launched_only).pack(side="left", padx=5)
        
        # Opciones dinámicas de escritorio
        self.desk_var = ctk.StringVar(value="Escritorio Actual")
        self.desk_combo = ctk.CTkComboBox(self.top_frame, variable=self.desk_var, values=["Escritorio Actual"], width=140)
        self.desk_combo.pack(side="left", padx=5)
        
        ctk.CTkButton(self.top_frame, text="Sel. Escritorio", width=100, command=self.select_chosen_desktop).pack(side="left", padx=5)
        
        ctk.CTkButton(self.top_frame, text="Todos", width=60, command=self.select_all).pack(side="left", padx=3)
        ctk.CTkButton(self.top_frame, text="Ninguno", width=60, command=self.select_none).pack(side="left", padx=3)

        # Scrollable Frame para la lista de ventanas
        self.scroll_frame = ctk.CTkScrollableFrame(self)
        self.scroll_frame.pack(fill="both", expand=True, padx=20, pady=10)

        # Bottom Frame
        self.bottom_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.bottom_frame.pack(fill="x", padx=20, pady=(5, 15))
        
        ctk.CTkButton(self.bottom_frame, text="Cancelar", width=100, fg_color="#555", command=self.destroy).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.bottom_frame, text="🧹 Cerrar Seleccionadas", width=180, fg_color="#AA0000", hover_color="#770000", command=self.close_selected).pack(side="right")

        self.windows_data = [] # stores dicts {"hwnd": hwnd, "title": title, "var": ctk.BooleanVar, "desktop_id": desktop_id, "kw_match": bool}
        self.current_desktop_id = None
        self.desktops_map = {} # Nombre Amigable -> GUID
        
        self.load_windows()
        self.populate_desktops_combo()
        self.populate_list()
        self.select_config_only()

    def populate_desktops_combo(self):
        opts = []
        if self.current_desktop_id and "Tu Escritorio Actual" not in opts:
            self.desktops_map["Tu Escritorio Actual"] = self.current_desktop_id
            opts.append("Tu Escritorio Actual")
            
        for name, guid in self.desktops_map.items():
            if name != "Tu Escritorio Actual" and name not in opts:
                opts.append(name)
                
        if opts:
            self.desk_combo.configure(values=opts)
            self.desk_var.set(opts[0])

    def get_config_keywords_and_paths(self):
        kws = []
        paths = []
        items = self.parent_app.apps_data.get(self.parent_app.current_category, [])
        for item in items:
            t = item.get('type')
            p = item.get('path', '')
            c = item.get('cmd', '')
            if not p and not c: continue
            
            if p: paths.append(os.path.normpath(p).lower())
            
            base = os.path.basename(p) if p else ""
            if t == 'vscode':
                if p: kws.append(base.lower())
                kws.append("visual studio code")
            elif t == 'ide':
                if p: kws.append(base.lower())
            elif t == 'obsidian':
                if p: kws.append(base.lower())
                kws.append("obsidian")
            elif t == 'powershell':
                if p: kws.append(base.lower())
                kws.append("terminal")
                kws.append("powershell")
            elif t == 'url':
                try:
                    def _extract_domain_kw(url_str):
                        d = url_str.replace("https://", "").replace("http://", "").split("/")[0]
                        d = d.replace("www.", "")
                        return d.split(".")[0] if "." in d else d

                    if p: kws.append(_extract_domain_kw(p).lower())
                    if c:
                        for chunk in c.split(TAB_SEPARATOR):
                            clean_c = chunk.strip()
                            if clean_c.startswith("http"):
                                kws.append(_extract_domain_kw(clean_c).lower())
                    
                    # Add browser keywords if defined
                    b_cmd = item.get('browser', '').lower()
                    if b_cmd and b_cmd != 'default':
                        b_exe = os.path.basename(b_cmd).replace(".exe", "")
                        kws.append(b_exe)
                    else:
                        # Fallback common browsers that could be opened
                        kws.extend(["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"])
                except: pass
            elif t == 'exe' or t == 'app' or not t:
                if p: kws.append(base.replace(".exe", "").lower())
                
        # Filtrar kws muy cortas para no hacer falsos positivos, excepto si es algo específico
        return [k for k in kws if len(k) > 2], paths

    def get_process_path(self, hwnd):
        try:
            import win32process
            import ctypes
            from ctypes import wintypes
            
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
            
            kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
            hProcess = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
            if not hProcess: return ""
            
            exe_path = ctypes.create_unicode_buffer(260)
            size = wintypes.DWORD(260)
            success = kernel32.QueryFullProcessImageNameW(hProcess, 0, exe_path, ctypes.byref(size))
            kernel32.CloseHandle(hProcess)
            
            if success:
                return os.path.normpath(exe_path.value).lower()
        except: pass
        return ""

    def load_windows(self):
        if not WINDOWS_LIBS_AVAILABLE:
            messagebox.showwarning("Aviso", "Librerías de Windows no disponibles para leer ventanas.")
            return

        import win32gui
        import win32con
        from pyvda import AppView, get_virtual_desktops, VirtualDesktop
        
        try:
            current_desk = VirtualDesktop.current()
            self.current_desktop_id = str(current_desk.id).upper()
            if not self.current_desktop_id.startswith("{"): self.current_desktop_id = "{" + self.current_desktop_id + "}"
        except:
            self.current_desktop_id = None

        desk_names_map = {}
        try:
            for i, d in enumerate(get_virtual_desktops()):
                g = str(d.id).upper()
                if not g.startswith("{"): g = "{" + g + "}"
                name = d.name if d.name else f"Escritorio {i+1}"
                desk_names_map[g] = name
                self.desktops_map[name] = g
        except: pass

        kws, conf_paths = self.get_config_keywords_and_paths()
        
        # Obtener HWNDs registrados en zone_stacks (ventanas lanzadas por la config actual)
        launched_hwnds = set()
        if hasattr(self.parent_app, 'zone_stacks'):
            for stack in self.parent_app.zone_stacks.values():
                for h in stack:
                    launched_hwnds.add(h)
        
        # Obtener el HWND propio del launcher y del diálogo para excluirlos con seguridad
        own_hwnd = None
        try:
            own_hwnd = self.parent_app.winfo_id()
        except: pass
        dialog_hwnd = None
        try:
            dialog_hwnd = self.winfo_id()
        except: pass
        
        # Procesos de sistema que nunca se deben cerrar
        SYSTEM_PROCESSES = {
            'explorer.exe', 'searchhost.exe', 'startmenuexperiencehost.exe',
            'shellexperiencehost.exe', 'textinputhost.exe', 'systeminformer.exe',
            'taskmgr.exe', 'applicationframehost.exe', 'widgets.exe',
            'lockapp.exe', 'runtimebroker.exe', 'dwm.exe', 'csrss.exe',
            'powertoys.exe', 'powertoys.fancyzones.exe'
        }
        SYSTEM_TITLES = {'program manager', 'settings', 'configuración', 'microsoft text input application'}
        
        # Proceso propio (para excluir python.exe que ejecuta este script)
        own_pid = os.getpid()

        def enum_windows_proc(hwnd, lParam):
            if win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd):
                title = win32gui.GetWindowText(hwnd)
                if not title: return
                if title.lower() in SYSTEM_TITLES: return
                
                # Excluir el propio launcher y este diálogo por HWND (más fiable que título)
                if hwnd == own_hwnd or hwnd == dialog_hwnd: return
                
                # Excluir por PID propio
                try:
                    import win32process
                    _, pid = win32process.GetWindowThreadProcessId(hwnd)
                    if pid == own_pid: return
                except: pass
                
                # Excluir procesos de sistema
                p_path = self.get_process_path(hwnd)
                if p_path:
                    proc_name = os.path.basename(p_path).lower()
                    if proc_name in SYSTEM_PROCESSES: return
                
                try:
                    view = AppView(hwnd)
                    desk_id = str(view.desktop_id).upper()
                    if not desk_id.startswith("{"): desk_id = "{" + desk_id + "}"
                except:
                    desk_id = "Unk"
                
                desk_name = desk_names_map.get(desk_id, desk_id)
                t_lower = title.lower()
                
                # Obtener path exacto del proceso para que las APPs/EXEs casen 100% de forma segura
                p_path = self.get_process_path(hwnd)
                
                matched = False
                # PRIORIDAD 1: Si está en zone_stacks, fue lanzada por la config → match directo
                if hwnd in launched_hwnds:
                    matched = True
                # PRIORIDAD 2: Coincidencia por path del proceso
                if not matched and p_path and p_path in conf_paths:
                    matched = True
                # PRIORIDAD 3: Coincidencia por keywords en el título
                if not matched and any(kw in t_lower for kw in kws):
                    matched = True
                
                self.windows_data.append({
                    "hwnd": hwnd,
                    "title": title,
                    "process_path": p_path,
                    "desktop_id": desk_id,
                    "desktop_name": desk_name,
                    "kw_match": matched,
                    "launched": hwnd in launched_hwnds,
                    "var": ctk.BooleanVar(value=False)
                })

        win32gui.EnumWindows(enum_windows_proc, 0)
        # Ordenar: primero las lanzadas, luego las de config, luego el resto
        self.windows_data.sort(key=lambda x: (not x["launched"], not x["kw_match"], x["desktop_name"], x["title"]))

    def populate_list(self):
        for w in self.scroll_frame.winfo_children(): w.destroy()
            
        for wdata in self.windows_data:
            row = ctk.CTkFrame(self.scroll_frame, fg_color="transparent")
            row.pack(fill="x", pady=2)
            
            chk = ctk.CTkCheckBox(row, text="", variable=wdata["var"], width=30)
            chk.pack(side="left", padx=5)
            
            # Indicador de origen
            if wdata["launched"]:
                lbl_origin = ctk.CTkLabel(row, text="🚀", width=22, font=("Roboto", 12))
                lbl_origin.pack(side="left", padx=(0, 3))
            
            lbl_title = ctk.CTkLabel(row, text=wdata["title"], anchor="w")
            lbl_title.pack(side="left", padx=5, fill="x", expand=True)
            
            # Mostrar nombre del proceso para identificación
            proc_name = os.path.basename(wdata.get("process_path", "")) if wdata.get("process_path") else "?"
            lbl_proc = ctk.CTkLabel(row, text=f"({proc_name})", width=100, text_color="#888", font=("Roboto", 11))
            lbl_proc.pack(side="right", padx=2)
            
            lbl_desk = ctk.CTkLabel(row, text=f"[{wdata['desktop_name']}]", width=120, text_color="gray")
            lbl_desk.pack(side="right", padx=5)

            if wdata["launched"]:
                lbl_title.configure(text_color="#2CC985")
                chk.select()
            elif wdata["kw_match"]:
                lbl_title.configure(text_color="#87CEEB")

    def select_launched_only(self):
        """Seleccionar SOLO las ventanas que el launcher abrió en esta sesión."""
        for w in self.windows_data:
            w["var"].set(w["launched"])

    def select_config_only(self):
        for w in self.windows_data:
            if w["kw_match"]: w["var"].set(True)
            else: w["var"].set(False)

    def select_chosen_desktop(self):
        sel_name = self.desk_var.get()
        target_id = self.desktops_map.get(sel_name)
        if not target_id: return
        
        for w in self.windows_data:
            if w["desktop_id"] == target_id: w["var"].set(True)
            else: w["var"].set(False)

    def select_all(self):
        for w in self.windows_data:
            w["var"].set(True)

    def select_none(self):
        for w in self.windows_data:
            w["var"].set(False)

    def close_selected(self):
        import win32gui
        import win32con
        
        selected = [w for w in self.windows_data if w["var"].get()]
        if not selected:
            messagebox.showwarning("Aviso", "No hay ventanas seleccionadas.", parent=self)
            return
            
        if not messagebox.askyesno("Confirmar", f"¿Cerrar {len(selected)} ventanas seleccionadas?", parent=self):
            return
        
        count = 0
        closed_hwnds = []
        for w in selected:
            try:
                win32gui.PostMessage(w["hwnd"], win32con.WM_CLOSE, 0, 0)
                count += 1
                closed_hwnds.append(w["hwnd"])
            except Exception as e:
                print(f"Error cerrando {w['title']}: {e}")
        
        # Limpiar ventanas cerradas de zone_stacks para que la rotación no intente activarlas
        if hasattr(self.parent_app, 'zone_stacks'):
            for z_key in list(self.parent_app.zone_stacks.keys()):
                self.parent_app.zone_stacks[z_key] = [
                    h for h in self.parent_app.zone_stacks[z_key] if h not in closed_hwnds
                ]
                # Eliminar stacks vacíos
                if not self.parent_app.zone_stacks[z_key]:
                    del self.parent_app.zone_stacks[z_key]
        
        messagebox.showinfo("Limpieza Completada", f"Se ha solicitado el cierre de {count} ventanas.", parent=self)
        self.destroy()

class HotkeysEditorDialog(ctk.CTkToplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Editor de Atajos y Modificadores de Ratón")
        self.geometry("700x650")
        self.transient(parent)
        self.grab_set()
        self.parent_app = parent
        
        self.hotkeys = dict(parent.hotkeys_data)
        
        # Diccionario de descripciones amigables (Simplificado)
        self.desc_map = {
            "mouse_cycle_fwd": "Ciclo: Siguiente Pestaña (Ratón Lateral o Teclado)",
            "mouse_cycle_bwd": "Ciclo: Anterior Pestaña (Ratón Lateral o Teclado)",
            "desktop_cycle_fwd": "Escritorio Virtual: Siguiente",
            "desktop_cycle_bwd": "Escritorio Virtual: Anterior",
            "util_reload_layouts": "Sistema: Recargar Layouts (Ctrl+Alt+L)"
        }
        
        ctk.CTkLabel(self, text="Configuración de Atajos Personalizados", font=("Roboto", 18, "bold")).pack(pady=(15, 5))
        ctk.CTkLabel(self, text="Instrucciones: Para editar un atajo pulsa en su botón 'Cambiar' y luego realiza \nla combinación deseada en tu teclado y/o ratón.", text_color="#aaa").pack(pady=(0, 10))
        
        # --- Nota Informativa (Bypass Navegación X1/X2) ---
        info_frame = ctk.CTkFrame(self, fg_color="#1E3A5F", corner_radius=8, border_width=1, border_color="#3A75C4")
        info_frame.pack(fill="x", padx=20, pady=(0, 15))
        
        info_text = (
            "💡 TIP DE NAVEGACIÓN (Botones Laterales X1 / X2)\n\n"
            "El Launcher bloquea automáticamente la navegación web ('Atrás/Adelante') al usar los botones \n"
            "laterales de forma aislada para evitar que se te cambien las pestañas/páginas sin querer.\n\n"
            "Si REALMENTE quieres navegar Atrás o Adelante en Chrome, VS Code, etc., simplemente\n"
            "mantén pulsado CTRL, ALT o SHIFT + Botón Lateral. El evento pasará limpio sin lag."
        )
        ctk.CTkLabel(info_frame, text=info_text, font=("Roboto", 12), text_color="#E0E0E0", justify="left").pack(padx=15, pady=10)

        # --- Switches de activación ---
        toggle_frame = ctk.CTkFrame(self, fg_color="#2B2B2B", corner_radius=8)
        toggle_frame.pack(fill="x", padx=20, pady=(0, 10))
        ctk.CTkLabel(toggle_frame, text="Módulos Activos", font=("Roboto", 14, "bold")).pack(anchor="w", padx=10, pady=(8, 4))
        
        sw_row1 = ctk.CTkFrame(toggle_frame, fg_color="transparent")
        sw_row1.pack(fill="x", padx=10, pady=2)
        self.zone_cycle_var = ctk.BooleanVar(value=parent.hotkeys_data.get("_zone_cycle_enabled", True))
        ctk.CTkSwitch(sw_row1, text="Navegación entre ventanas/pestañas (Zone Cycling)", 
                       variable=self.zone_cycle_var, onvalue=True, offvalue=False,
                       fg_color="#555", progress_color="#2CC985").pack(anchor="w")
        
        sw_row2 = ctk.CTkFrame(toggle_frame, fg_color="transparent")
        sw_row2.pack(fill="x", padx=10, pady=2)
        self.desktop_cycle_var = ctk.BooleanVar(value=parent.hotkeys_data.get("_desktop_cycle_enabled", True))
        ctk.CTkSwitch(sw_row2, text="Cambio de escritorios virtuales", 
                       variable=self.desktop_cycle_var, onvalue=True, offvalue=False,
                       fg_color="#555", progress_color="#2CC985").pack(anchor="w")

        sw_row3 = ctk.CTkFrame(toggle_frame, fg_color="transparent")
        sw_row3.pack(fill="x", padx=10, pady=(2, 8))
        self.pip_watcher_var = ctk.BooleanVar(value=parent._pip_watcher_active)
        ctk.CTkSwitch(sw_row3, text="Auto-anclar PiP (ventana flotante) en todos los escritorios", 
                       variable=self.pip_watcher_var, onvalue=True, offvalue=False,
                       fg_color="#555", progress_color="#2CC985").pack(anchor="w")
        
        self.scroll = ctk.CTkScrollableFrame(self, fg_color="#2B2B2B")
        self.scroll.pack(fill="both", expand=True, padx=20, pady=5)
        
        self.vars = {}
        for key, desc in self.desc_map.items():
            row = ctk.CTkFrame(self.scroll, fg_color="#333", corner_radius=5)
            row.pack(fill="x", pady=4, padx=5)
            
            ctk.CTkLabel(row, text=desc, width=280, anchor="w", font=("Roboto", 12)).pack(side="left", padx=10, pady=8)
            
            curr_val = self.hotkeys.get(key, "Ninguno")
            v = ctk.StringVar(value=curr_val)
            self.vars[key] = v
            
            lbl_val = ctk.CTkLabel(row, textvariable=v, width=150, fg_color="#444", corner_radius=4, text_color="#2CC985", font=("Roboto", 12, "bold"))
            lbl_val.pack(side="left", padx=10)
            
            ctk.CTkButton(row, text="Cambiar", width=80, fg_color="#5A5A5A", hover_color="#7A7A7A",
                          command=lambda k=key, tv=v: self.start_recording(k, tv)).pack(side="left", padx=5)
            
        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=20, pady=15)
        ctk.CTkButton(btn_frame, text="Cancelar", fg_color="#555", command=self.destroy, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(btn_frame, text="Guardar Atajos y Reiniciar Listeners", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=250).pack(side="right")

    def start_recording(self, key, text_var):
        d = RecordHotkeyDialog(self, key, text_var)
        self.wait_window(d)

    def save(self):
        for k, v in self.vars.items():
            self.parent_app.hotkeys_data[k] = v.get()
        self.parent_app.hotkeys_data["_zone_cycle_enabled"] = self.zone_cycle_var.get()
        self.parent_app.hotkeys_data["_desktop_cycle_enabled"] = self.desktop_cycle_var.get()
        
        # PiP Watcher: activar/desactivar según el switch
        pip_desired = self.pip_watcher_var.get()
        if pip_desired and not self.parent_app._pip_watcher_active:
            self.parent_app._start_pip_watcher()
        elif not pip_desired and self.parent_app._pip_watcher_active:
            self.parent_app._stop_pip_watcher()
        
        self.parent_app._save_data()
        
        messagebox.showinfo("Guardado", "Configuración guardada ✅")
        self.destroy()

class RecordHotkeyDialog(ctk.CTkToplevel):
    def __init__(self, parent, action_key, text_var):
        super().__init__(parent)
        self.title("Grabar Atajo")
        self.geometry("450x300")
        self.transient(parent)
        self.grab_set()
        
        self.action_key = action_key
        self.text_var = text_var
        
        ctk.CTkLabel(self, text="Escuchando...", font=("Roboto", 18, "bold"), text_color="#2CC985").pack(pady=(30, 10))
        ctk.CTkLabel(self, text="Pulsa la combinación de teclado, O mantén tus modificadores + Botón del Ratón.\n(Los clics comunes primarios sin modificadores se ignoran para no molestar la UI).", text_color="#aaa").pack(pady=10)
            
        self.lbl_result = ctk.CTkLabel(self, text="Esperando entrada...", font=("Roboto", 14), fg_color="#333", width=300, corner_radius=5)
        self.lbl_result.pack(pady=20, ipady=10)
        
        self.btn_save = ctk.CTkButton(self, text="Aceptar y Cerrar", command=self.apply, state="disabled")
        self.btn_save.pack(pady=10)
        
        self.current_combo = ""
        self._listener_thread = None
        self._stop_listening = False

        self.start_listening()
        self.protocol("WM_DELETE_WINDOW", self.on_close)

    def start_listening(self):
        import threading
        
        def listener_task():
            from pynput import keyboard, mouse
            import time
            
            recorded_keys = set()
            kb_state = {"ctrl": False, "alt": False, "shift": False, "win": False}
            
            def get_mods():
                mods = []
                for m in ['ctrl', 'alt', 'shift', 'win']:
                    if kb_state[m] or m in recorded_keys: mods.append(m)
                return list(dict.fromkeys(mods))
                
            def on_press(key):
                if self._stop_listening: return False
                kname = getattr(key, 'name', None)
                if kname:
                    if kname in ['ctrl_l', 'ctrl_r']: 
                        kb_state['ctrl'] = True
                        kname = 'ctrl'
                    elif kname in ['alt_l', 'alt_r', 'alt_gr']: 
                        kb_state['alt'] = True
                        kname = 'alt'
                    elif kname in ['shift', 'shift_r']: 
                        kb_state['shift'] = True
                        kname = 'shift'
                    elif kname in ['cmd', 'cmd_r', 'cmd_l', 'win']: 
                        kb_state['win'] = True
                        kname = 'win'
                else:
                    kname = getattr(key, 'char', str(key))
                    if kname and kname.startswith('<') and kname.endswith('>'):
                        kname = kname[1:-1]
                        
                if kname: recorded_keys.add(kname)
                
                mods = get_mods()
                others = [k for k in recorded_keys if k not in ['ctrl', 'alt', 'shift', 'win']]
                
                if others:
                    self.current_combo = "+".join(mods + others)
                    self.after(0, lambda: self.btn_save.configure(state="normal", fg_color="#2CC985"))
                else:
                    self.current_combo = "+".join(mods)
                    
                self.after(0, lambda: self.lbl_result.configure(text=self.current_combo.upper()))

            def on_release(key):
                if self._stop_listening: return False
                kname = getattr(key, 'name', None)
                if kname in ['ctrl_l', 'ctrl_r']: kb_state['ctrl'] = False
                elif kname in ['alt_l', 'alt_r', 'alt_gr']: kb_state['alt'] = False
                elif kname in ['shift', 'shift_r']: kb_state['shift'] = False
                elif kname in ['cmd', 'cmd_r', 'cmd_l', 'win']: kb_state['win'] = False
                
            def on_click(x, y, button, pressed):
                if self._stop_listening: return False
                if pressed:
                    btn_name = button.name
                    mods = get_mods()
                    # Ignoramos clic izquierdo puro para que el usuario pueda usar la interfaz gráfica
                    if btn_name == 'left' and not mods:
                        return True
                        
                    # Prefijamos los botones simples del ratón para no confundirlos con las flechas del teclado
                    if btn_name not in ['x1', 'x2']:
                        btn_name = 'mouse_' + btn_name
                    
                    # Calcular teclas extra no-modificadoras que el usuario haya pulsado en el teclado
                    kb_others = [k for k in recorded_keys if k not in ['ctrl', 'alt', 'shift', 'win']]
                    if kb_others:
                        self.current_combo = "+".join(mods + kb_others + [btn_name])
                    else:
                        self.current_combo = "+".join(mods + [btn_name])
                        
                    self.after(0, lambda: self.lbl_result.configure(text=self.current_combo.upper()))
                    self.after(0, lambda: self.btn_save.configure(state="normal", fg_color="#2CC985"))
                    self._stop_listening = True
                    return False
                    
            k_listener = keyboard.Listener(on_press=on_press, on_release=on_release)
            m_listener = mouse.Listener(on_click=on_click)
            
            k_listener.start()
            m_listener.start()
            
            while not self._stop_listening: time.sleep(0.1)
            
            k_listener.stop()
            m_listener.stop()
            
        self._listener_thread = threading.Thread(target=listener_task, daemon=True)
        self._listener_thread.start()
        
    def apply(self):
        if self.current_combo:
            self.text_var.set(self.current_combo)
        self.on_close()
        
    def on_close(self):
        self._stop_listening = True
        self.destroy()

class RecoverySelectionDialog(ctk.CTkToplevel):
    def __init__(self, parent, unmatched_intents, on_confirm_callback):
        super().__init__(parent)
        self.title("Revisar Recuperación de Entorno")
        self.geometry("800x650")
        self.minsize(700, 500)
        self.transient(parent)
        self.grab_set()

        self.on_confirm_callback = on_confirm_callback
        self.intents_data = unmatched_intents
        
        self.checkbox_vars = {}

        self.lbl_title = ctk.CTkLabel(self, text="Ventanas detectadas para recuperar. Puedes elegir cuáles no procesar\ny también REABRIR aquellas app que hayas cerrado sin querer:", font=("Roboto", 14, "bold"))
        self.lbl_title.pack(pady=(15, 5), padx=20, anchor="w")

        self.scroll_frame = ctk.CTkScrollableFrame(self, fg_color="transparent")
        self.scroll_frame.pack(fill="both", expand=True, padx=20, pady=10)

        import win32gui
        
        groups = {}
        for intent in self.intents_data:
            hwnds = intent.get("_matched_hwnds", [])
            z_name = intent.get("fancyzone", "Sin Zona Asignada")
            
            if z_name not in groups:
                groups[z_name] = []
                
            has_strong_match = any(score >= 8 for h, score in hwnds) if hwnds else False
            
            if not has_strong_match:
                # App is missing completely, or only has generic matches
                import os
                p = intent.get('path', '')
                c = intent.get('cmd', '')
                name = os.path.basename(p) if p else (c[:30] + '...' if c else 'App')
                groups[z_name].append({
                    "id": id(intent),
                    "title": f"⚠️ [Cerrada] Reabrir: {name} ({intent.get('type', 'app')})",
                    "intent": intent,
                    "is_missing": True,
                    "is_weak": False
                })
            
            for hwnd, score in hwnds:
                try: 
                    t = win32gui.GetWindowText(hwnd)
                    if not t: t = f"Ventana Desconocida (HWND: {hwnd})"
                    
                    import win32process, os, psutil
                    try:
                        _, pid = win32process.GetWindowThreadProcessId(hwnd)
                        p_name = psutil.Process(pid).name().lower()
                    except: p_name = "desconocido"
                except: 
                    t = f"Ventana Desconocida (HWND: {hwnd})"
                    p_name = "desconocido"
                
                is_weak = score < 8
                title_display = f"[{p_name}] {t}" if is_weak else t
                if is_weak:
                    title_display = f"🔍 {title_display}"
                
                groups[z_name].append({
                    "id": hwnd,
                    "title": title_display,
                    "intent": intent,
                    "is_missing": False,
                    "is_weak": is_weak
                })

        if not groups:
            ctk.CTkLabel(self.scroll_frame, text="No se encontraron aplicaciones configuradas.", font=("Roboto", 12, "italic"), text_color="gray").pack(pady=20)
        else:
            for g_name, items in groups.items():
                g_frame = ctk.CTkFrame(self.scroll_frame, fg_color="#2B2B2B", corner_radius=8)
                g_frame.pack(fill="x", pady=5, padx=5)
                
                ctk.CTkLabel(g_frame, text=g_name, font=("Roboto", 13, "bold"), text_color="#4da6ff").pack(anchor="w", padx=10, pady=(10, 5))
                
                for item in items:
                    ident = item["id"]
                    is_missing = item["is_missing"]
                    is_weak = item.get("is_weak", False)
                    
                    # Por defecto marcamos las exactas, desmarcamos las genéricas y las desaparecidas
                    default_check = not is_missing and not is_weak
                    var = tk.BooleanVar(value=default_check) 
                    
                    self.checkbox_vars[ident] = (var, item["intent"], is_missing)
                    
                    color = "white"
                    if is_missing: color = "#ffb366"
                    elif is_weak: color = "#a6a6a6"
                    
                    cb = ctk.CTkCheckBox(g_frame, text=item["title"], variable=var, font=("Roboto", 12), text_color=color)
                    cb.pack(anchor="w", padx=20, pady=2)
                
                ctk.CTkLabel(g_frame, text="", height=5).pack()

        self.btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=15)
        
        self.btn_cancel = ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#444444", hover_color="#333333", command=self.destroy, width=120)
        self.btn_cancel.pack(side="left")
        
        self.btn_confirm = ctk.CTkButton(self.btn_frame, text="Confirmar Recuperación", fg_color="#2CC985", hover_color="#24A36B", command=self.confirm, width=180)
        self.btn_confirm.pack(side="right")

    def confirm(self):
        approved_hwnds = set()
        to_relaunch = []
        
        for ident, (var, intent, is_missing) in self.checkbox_vars.items():
            if var.get():
                if is_missing:
                    to_relaunch.append(intent)
                else:
                    approved_hwnds.add(ident)
        
        for intent in self.intents_data:
            if "_matched_hwnds" in intent:
                intent["_matched_hwnds"] = [h for h, score in intent["_matched_hwnds"] if h in approved_hwnds]
            if intent in to_relaunch:
                intent["_relaunch"] = True
                
        self.on_confirm_callback(self.intents_data)
        self.destroy()

# ─────────────────────────────────────────────────────────────────────────────
#  APP PRINCIPAL
# ─────────────────────────────────────────────────────────────────────────────
class DevLauncherApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("PRO Workspace Launcher")
        self.geometry("1150x800")
        self.minsize(900, 600)
        
        # Cargar icono (desde archivo o embebido)
        eff_path = get_effective_icon_path()
        if eff_path:
            try:
                self.iconbitmap(eff_path)
            except Exception as e:
                print(f"No se pudo cargar el icono: {e}")
        
        self.db_file = os.path.join(APP_DIR, "mis_apps_config_v2.json")
        self.fancyzones_path = os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\PowerToys\FancyZones")
        self.default_layout_name = None

        self.apps_data = {
            "Desarrollo": [], "Navegación": [],
            "Edición": [], "Ocio": [],
            "Otro": []
        }
        self.current_category = "Desarrollo"
        self.available_layouts = {}
        self.available_monitors = []
        self.applied_mappings = {}
        self.fz_layouts_cache = {} # Cache de definiciones de layouts para portabilidad

        # Estado global de modificadores para Pynput
        self.kb_modifiers = {"ctrl": False, "alt": False, "shift": False, "win": False}
        self.mouse_btn_states = {"left": False, "right": False, "middle": False}

        # --- MOTOR CUSTOM DE ZONAS (Sustituto de FZ Runtime) ---
        self.zone_stacks = {} # {(d_guid, m_dev, l_uuid, z_idx): [hwnd1, hwnd2]}
        self.hotkeys_active = False
        
        # --- NUEVO: Gestor de Hooks Win32 para supresión total de X1/X2 ---
        self.hook_manager = GlobalHookManager()
        self.hook_manager.start()
        
        self._start_global_hotkeys()
        # ----------------------------------------------------

        # --- PiP WATCHER (Anclar ventana flotante a todos los escritorios) ---
        self._pip_watcher_active = False
        self._pip_watcher_thread = None
        self._pip_pinned_hwnds = set()  # HWNDs ya anclados para no repetir
        # -------------------------------------------------------------------

        self.protocol("WM_DELETE_WINDOW", self.on_app_close)

        self._load_data()
        self.load_fancyzones_layouts()

        # --- HEADER ---
        self.header_frame = ctk.CTkFrame(self)
        self.header_frame.pack(fill="x", padx=20, pady=(20, 10))
        
        ctk.CTkLabel(self.header_frame, text="Pro Launcher - Modo:", font=("Roboto", 16, "bold")).pack(side="left", padx=10)
        self.category_option = ctk.CTkOptionMenu(self.header_frame, values=[], command=self.change_category, width=200)
        self.category_option.pack(side="left", padx=5)
        
        ctk.CTkButton(self.header_frame, text="Renombrar", width=80, fg_color="#555", command=self.rename_category_dialog).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="Duplicar", width=80, fg_color="#555", command=self.duplicate_category_dialog).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="🗑️", width=40, fg_color="#AA0000", hover_color="#770000", command=self.delete_category).pack(side="left", padx=5)
        
        self.btn_hotkeys = ctk.CTkButton(self.header_frame, text="⚙️ Atajos/ Ratón", width=120, fg_color="#444", hover_color="#555", command=self.open_hotkeys_editor)
        self.btn_hotkeys.pack(side="right", padx=10, pady=10)
        
        ctk.CTkButton(self.header_frame, text="+ Nueva", width=80, command=self.add_category_dialog).pack(side="right", padx=10)

        # --- POWERTOYS CONFIG ---
        self.pt_frame = ctk.CTkFrame(self)
        self.pt_frame.pack(fill="x", padx=20, pady=(0, 10))
        
        top_pt_bar = ctk.CTkFrame(self.pt_frame, fg_color="transparent")
        top_pt_bar.pack(fill="x", pady=(5, 0))
        
        ctk.CTkLabel(top_pt_bar, text="FancyZones Base:", font=("Roboto", 14, "bold"), width=120, anchor="w").pack(side="left", padx=(10, 5))
        self.fz_path_var = ctk.StringVar(value=self.fancyzones_path)
        self.fz_path_entry = ctk.CTkEntry(top_pt_bar, textvariable=self.fz_path_var)
        self.fz_path_entry.pack(side="left", padx=5, fill="x", expand=True)
        
        btn_info = ctk.CTkButton(top_pt_bar, text="?", width=30, fg_color="#444", hover_color="#666", command=self.show_fz_info)
        btn_info.pack(side="left", padx=5)
        
        ctk.CTkButton(top_pt_bar, text="Guardar/Recargar", width=120, fg_color="#007ACC", hover_color="#005A9E", command=self.save_fz_path).pack(side="right", padx=10)
        
        bot_pt_bar = ctk.CTkFrame(self.pt_frame, fg_color="transparent")
        bot_pt_bar.pack(fill="x", pady=(5, 5))
        ctk.CTkButton(bot_pt_bar, text="🖥️ Asignar Distribuciones por Pantalla/Escritorio", 
                      fg_color="#4B4B4B", hover_color="#333", command=self.open_assigner).pack(side="right", padx=10)

        # --- FANCY ZONES SYNC WARNING ---
        self.fz_warning_frame = ctk.CTkFrame(self, fg_color="transparent")
        # No empaquetar por defecto, se empaqueta si hay discrepancias en _update_fz_warning
        
        self.fz_warning_inner = ctk.CTkFrame(self.fz_warning_frame, fg_color="#2B2B2B", border_width=1, border_color="#E5A00D")
        
        self.fz_warning_label = ctk.CTkLabel(self.fz_warning_inner, text="", font=("Roboto", 12),
                                              text_color="#FFD700", anchor="w", wraplength=700)
        self.fz_warning_label.pack(side="left", fill="x", expand=True, padx=(10, 5), pady=8)
        
        self.btn_fz_sync = ctk.CTkButton(self.fz_warning_inner, text="🔄 Sincronizar Layouts", 
                                          width=160, height=30, font=("Roboto", 11, "bold"),
                                          fg_color="#E5A00D", hover_color="#B57B02", text_color="#000",
                                          command=self._on_sync_fz_click)
        self.btn_fz_sync.pack(side="right", padx=(5, 10), pady=8)

        # --- LANZAR Y LIMPIAR ---
        self.action_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.action_frame.pack(fill="x", padx=20, pady=10)
        
        self.btn_launch = ctk.CTkButton(self.action_frame, text="🚀 LANZAR ENTORNO", height=50, font=("Roboto", 14, "bold"), 
                                        fg_color="#2CC985", hover_color="#24A36B", command=self.launch_workspace)
        self.btn_launch.pack(side="left", fill="x", expand=True, padx=(0, 5))

        self.btn_recover = ctk.CTkButton(self.action_frame, text="🔄", height=50, width=50, font=("Roboto", 18, "bold"), 
                                        fg_color="#007ACC", hover_color="#005A9E", command=self.recover_workspace)
        self.btn_recover.pack(side="left", padx=(5, 0))
        
        self.btn_recover_info = ctk.CTkButton(self.action_frame, text="?", height=50, width=30, font=("Roboto", 14, "bold"), 
                                        fg_color="#444", hover_color="#666", command=self.show_recover_info)
        self.btn_recover_info.pack(side="left", padx=(5, 5))
        
        self.btn_clean_bottom = ctk.CTkButton(self.action_frame, text="🧹 LIMPIAR ENTORNO", height=50, font=("Roboto", 14, "bold"), 
                                       fg_color="#AA0000", hover_color="#770000", command=self.open_cleaner)
        self.btn_clean_bottom.pack(side="left", fill="x", expand=True, padx=(5, 0))

        # --- FOOTER ---
        self.footer_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.footer_frame.pack(side="bottom", fill="x", padx=20, pady=20)
        
        ctk.CTkButton(self.footer_frame, text="Añadir .EXE", width=90, command=self.add_exe).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Web", width=70, fg_color="#E5A00D", hover_color="#B57B02", command=self.add_url).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="IDE", width=90, fg_color="#007ACC", hover_color="#005A9E", command=self.add_ide_project).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Obsidian", width=90, fg_color="#7A3EE8", hover_color="#5D24B8", command=self.add_obsidian_vault).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Terminal (Tabs)", width=110, fg_color="#5A5A5A", hover_color="#333", command=self.add_powershell).pack(side="left", padx=5, expand=True, fill="x")

        # --- LISTA ---
        self.apps_frame = ctk.CTkScrollableFrame(self, label_text="Elementos configurados")
        self.apps_frame.pack(side="top", fill="both", expand=True, padx=20, pady=10)

        self.refresh_categories()
        # Comprobar layouts de FancyZones al iniciar
        self.after(500, self._update_fz_warning)

    def on_app_close(self):
        """Manejador principal de cierre de la aplicación para limpiar hooks y threads."""
        print("[App] Cerrando aplicación, deteniendo hooks...")
        if hasattr(self, 'hook_manager'):
            self.hook_manager.stop()
        
        self.hotkeys_active = False # Permitir que otros threads sepan que estamos cerrando
        self.destroy()

    # --- DATOS ---
    def _load_data(self):
        try:
            if os.path.exists(self.db_file):
                with open(self.db_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    self.apps_data = data.get("apps", self.apps_data)
                    self.last_saved_category = data.get("last_category", "Desarrollo")
                    self.current_category = self.last_saved_category
                    self.applied_mappings = data.get("applied_mappings", {})
                    self.fz_layouts_cache = data.get("fz_layouts_cache", {})
                    
                    # Restaurar estado del PiP watcher
                    pip_enabled = data.get("pip_watcher_enabled", False)
                    if pip_enabled:
                        self.after(500, self._start_pip_watcher)
                    
                    # Cargar Hotkeys: mezclar defaults con lo guardado para no perder nuevas claves
                    default_hotkeys = {
                        "cycle_forward": "ctrl+alt+pagedown",
                        "cycle_backward": "ctrl+alt+pageup",
                        "mouse_cycle_fwd": "alt+x1",
                        "mouse_cycle_bwd": "alt+x2",
                        "desktop_cycle_fwd": "x2",
                        "desktop_cycle_bwd": "x1",
                        "util_reload_layouts": "ctrl+alt+l"
                    }
                    saved_hk = data.get("hotkeys", {})
                    self.hotkeys_data = {**default_hotkeys, **saved_hk}
        except Exception as e:
            print(f"Error cargando base de datos: {e}")
            self.hotkeys_data = {
                "cycle_forward": "ctrl+alt+pagedown", "cycle_backward": "ctrl+alt+pageup",
                "mouse_cycle_fwd": "alt+x1", "mouse_cycle_bwd": "alt+x2",
                "desktop_cycle_fwd": "x2", "desktop_cycle_bwd": "x1",
                "util_reload_layouts": "ctrl+alt+l"
            }

    def _save_data(self):
        try:
            data = {
                "apps": self.apps_data,
                "last_category": self.current_category,
                "applied_mappings": self.applied_mappings,
                "fz_layouts_cache": self.fz_layouts_cache,
                "hotkeys": getattr(self, "hotkeys_data", {}),
                "pip_watcher_enabled": self._pip_watcher_active
            }
            with open(self.db_file, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=4, ensure_ascii=False)
        except Exception as e:
            messagebox.showerror("Error", f"No se pudo guardar: {e}")

    # --- POWERTOYS CONFIG METHODS ---
    def show_fz_info(self):
        msg = ("Normalmente la configuración de FancyZones está en:\n"
               "%LOCALAPPDATA%\\Microsoft\\PowerToys\\FancyZones\n\n"
               "Introduce la ruta donde se encuentre 'custom-layouts.json' y pulsa 'Guardar/Recargar'.")
        messagebox.showinfo("Ruta FancyZones", msg)

    def save_fz_path(self):
        self.fancyzones_path = self.fz_path_var.get()
        self.save_data()
        self.load_fancyzones_layouts()
        messagebox.showinfo("OK", f"Ruta guardada y layouts recargados.\nLayouts encontrados: {len(self.available_layouts)}\nActivo: {self.default_layout_name}")

    def load_fancyzones_layouts(self):
        self.available_layouts = {}
        self.available_monitors = ["Por defecto"]
        self.default_layout_name = None
        self.applied_mappings = {}
        if not self.fancyzones_path: return
        
        custom_json_path = os.path.join(self.fancyzones_path, "custom-layouts.json")
        applied_json_path = os.path.join(self.fancyzones_path, "applied-layouts.json")
        
        # Primero leer custom-layouts para tener la tabla UUID -> Name
        try:
            if os.path.exists(custom_json_path):
                with open(custom_json_path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                
                for layout in data.get("custom-layouts", []):
                    layout_name = layout.get("name", "Unnamed")
                    layout_uuid = str(layout.get("uuid", "")).strip("{}").lower()
                    
                    info = layout.get("info", {})
                    info["type"] = layout.get("type", "")
                    info["uuid"] = layout_uuid
                    self.available_layouts[layout_name] = info
                    # Actualizar caché persistente usando UUID como clave primaria (evita colisiones de nombres)
                    if layout_uuid:
                        self.fz_layouts_cache[layout_uuid] = layout.copy()
        except Exception as e:
            print(f"Error parseando custom-layouts JSON: {e}")

        # Intentar obtener el layout por defecto e historial dinámico aplicado
        default_uuid = None
        
        if os.path.exists(applied_json_path):
            try:
                with open(applied_json_path, 'r', encoding='utf-8') as f:
                    applied_data = json.load(f)
                    for app_layout in applied_data.get("applied-layouts", []):
                        al = app_layout.get("applied-layout", {})
                        uuid = str(al.get("uuid", "")).strip("{}").lower()
                        
                        if uuid and uuid != "00000000-0000-0000-0000-000000000000":
                            default_uuid = uuid
                            
                            # Enlace para autodetectar qué escritorio usa qué layout:
                            dev = app_layout.get("device", {})
                            vd_guid = dev.get("virtual-desktop", "").upper()
                            
                            if vd_guid:
                                lname = next((n for n, dt in self.available_layouts.items() if dt.get("uuid") == uuid), None)
                                if lname:
                                    mon_str = dev.get("monitor", "")
                                    mon_num = dev.get("monitor-number", "?")
                                    clean_mon = mon_str.replace("\\\\.\\", "").replace("DISPLAY", "Display ")
                                    if clean_mon == mon_str and "LOCALDISPLAY" in mon_str: clean_mon = "Display Principal"
                                    
                                    mon_friendly = f"Pantalla {mon_num} [{clean_mon}]"
                                    if mon_friendly not in self.available_monitors and mon_friendly != "Pantalla ? []":
                                        self.available_monitors.append(mon_friendly)
                                        
                                    self.applied_mappings[f"{vd_guid}_{mon_friendly}"] = lname
                                    
            except Exception as e: print("No se pudo leer applied-layouts:", e)

        if default_uuid:
            self.default_layout_name = next((n for n, dt in self.available_layouts.items() if dt.get("uuid") == default_uuid), None)

    def open_assigner(self):
        d = AssignLayoutsDialog(self)
        self.wait_window(d)
        if d.applied_data:
            self.load_fancyzones_layouts()

    def open_cleaner(self):
        d = CleanWorkspaceDialog(self)
        self.wait_window(d)
        
    def open_hotkeys_editor(self):
        d = HotkeysEditorDialog(self)
        self.wait_window(d)

    # --- CATEGORÍAS ---
    def refresh_categories(self):
        cats = list(self.apps_data.keys())
        if not cats:
            self.apps_data["General"] = []
            cats = ["General"]
            self._save_data()
        
        self.category_option.configure(values=cats)
        target = cats[0]
        if self.current_category in cats: target = self.current_category
        elif self.last_saved_category in cats:
            target = self.last_saved_category
            self.last_saved_category = None
            
        self.current_category = target
        self.category_option.set(target)
        self.refresh_apps_list()

    def change_category(self, choice):
        self.current_category = choice
        self._save_data()
        self.refresh_apps_list()
        # Comprobar si los layouts de FancyZones coinciden con esta categoría
        self.after(200, self._update_fz_warning)

    def add_category_dialog(self):
        if n := ctk.CTkInputDialog(text="Nombre:", title="Nueva").get_input():
            if n not in self.apps_data:
                self.apps_data[n] = []
                self.current_category = n
                self._save_data()
                self.refresh_categories()

    def rename_category_dialog(self):
        if not self.current_category: return
        if n := ctk.CTkInputDialog(text=f"Nuevo nombre:", title="Renombrar").get_input():
            if n not in self.apps_data:
                self.apps_data[n] = self.apps_data.pop(self.current_category)
                self.current_category = n
                self._save_data()
                self.refresh_categories()

    def duplicate_category_dialog(self):
        if not self.current_category: return
        n = ctk.CTkInputDialog(text=f"Nombre para la copia de '{self.current_category}':", title="Duplicar").get_input()
        if n:
            if n not in self.apps_data:
                # Creamos una copia profunda de los elementos actuales
                self.apps_data[n] = json.loads(json.dumps(self.apps_data[self.current_category]))
                self.current_category = n
                self._save_data()
                self.refresh_categories()
            else:
                messagebox.showerror("Error", "Ya existe una categoría con ese nombre.")

    def delete_category(self):
        if self.current_category and messagebox.askyesno("Borrar", f"¿Eliminar '{self.current_category}'?"):
            del self.apps_data[self.current_category]
            self.current_category = None
            self._save_data()
            self.refresh_categories()

    # --- LISTA ---
    def refresh_apps_list(self):
        for w in self.apps_frame.winfo_children(): w.destroy()
        items = self.apps_data.get(self.current_category, [])
        if not items:
            ctk.CTkLabel(self.apps_frame, text="Lista vacía.", text_color="gray").pack(pady=20)
            return

        for idx, item in enumerate(items):
            row = ctk.CTkFrame(self.apps_frame)
            row.pack(fill="x", pady=5, padx=5)
            
            t = item.get('type')
            p = item.get('path', '')
            cmd = item.get('cmd', '')

            if t == 'url': 
                num_tabs = cmd.count(TAB_SEPARATOR) + 1 if cmd else 1
                browser_display = item.get('browser_display', 'Edge')
                if browser_display.startswith("🖥️"): browser_display = "Default"
                elif browser_display.startswith("✏️"): browser_display = item.get('browser', 'Custom')
                tag, col, txt = "[WEB]", "#E5A00D", f"Web [{browser_display}] ({num_tabs} pest.): {p}"
            elif t == 'vscode': tag, col, txt = "[CODE]", "#007ACC", f"Proyecto: {os.path.basename(p)}"
            elif t == 'ide': 
                ide_cmd = str(item.get('ide_cmd', 'IDE')).upper()[:6]
                tag, col, txt = f"[{ide_cmd}]", "#007ACC", f"Proyecto: {os.path.basename(p)} ({item.get('ide_cmd')})"
            elif t == 'obsidian': tag, col, txt = "[OBS]", "#7A3EE8", f"Vault: {os.path.basename(p)}"
            elif t == 'powershell':
                # Contar pestañas
                num_tabs = cmd.count(TAB_SEPARATOR) + 1
                tag, col, txt = "[TERM]", "#5A5A5A", f"Terminal ({num_tabs} pestañas) en: {os.path.basename(p)}"
            else: tag, col, txt = "[APP]", "gray", os.path.basename(p)

            ctk.CTkLabel(row, text=tag, text_color=col, width=60, font=("Consolas", 12, "bold")).pack(side="left", padx=5)
            ctk.CTkLabel(row, text=txt, anchor="w").pack(side="left", padx=10, fill="x", expand=True)
            
            ctk.CTkButton(row, text="✏️", width=30, fg_color="#E5A00D", hover_color="#B57B02", command=lambda i=idx: self.edit_app_item(i)).pack(side="right", padx=2)
            
            ctk.CTkButton(row, text="X", width=30, fg_color="#FF5555", command=lambda i=idx: self.remove_item(i)).pack(side="right", padx=5)

    def edit_app_item(self, idx):
        item = self.apps_data[self.current_category][idx]
        dlg = AdvancedItemDialog(self, title="Editar Item", path_or_url=item.get("path", ""), item_type=item.get("type", "exe"), item_data=item)
        self.wait_window(dlg)
        if dlg.result:
            item.update(dlg.result)
            self._save_data()
            self.refresh_apps_list()

    # --- ADDERS ---
    def add_exe(self):
        if p := filedialog.askopenfilename(filetypes=[("Exe", "*.exe")]):
            dlg = AdvancedItemDialog(self, title="Configurar EXE", path_or_url=os.path.normpath(p), item_type="exe")
            self.wait_window(dlg)
            if dlg.result:
                self.add_item("exe", os.path.normpath(p), extras=dlg.result)
    
    def add_url(self):
        dlg = AddMultiWebDialog(self)
        self.wait_window(dlg)
        if dlg.result:
            self.add_item("url", dlg.result["path"], extras=dlg.result)

    def add_vscode_project(self):
        if p := filedialog.askdirectory(title="Proyecto VS Code"): 
            dlg = AdvancedItemDialog(self, title="Configurar Proyecto VSCode", path_or_url=os.path.normpath(p), item_type="vscode")
            self.wait_window(dlg)
            if dlg.result:
                self.add_item("vscode", os.path.normpath(p), extras=dlg.result)

    def add_ide_project(self):
        dlg = AddIDEDialog(self)
        self.wait_window(dlg)
        if dlg.result:
            path = dlg.result["path"]
            ide_cmd = dlg.result["ide_cmd"]
            
            # Pasar los datos iniciales al diálogo avanzado para que no se pierda el comando IDE
            initial_data = {"ide_cmd": ide_cmd}
            adv_dlg = AdvancedItemDialog(self, title=f"Configurar Proyecto ({ide_cmd})", 
                                         path_or_url=path, item_type="ide", item_data=initial_data)
            self.wait_window(adv_dlg)
            if adv_dlg.result:
                self.add_item("ide", path, extras=adv_dlg.result)
            
    def add_obsidian_vault(self):
        if p := filedialog.askdirectory(title="Vault Obsidian"):
            dlg = AdvancedItemDialog(self, title="Configurar Obsidian Vault", path_or_url=os.path.normpath(p), item_type="obsidian")
            self.wait_window(dlg)
            if dlg.result:
                self.add_item("obsidian", os.path.normpath(p), extras=dlg.result)

    def add_powershell(self):
        if p := filedialog.askdirectory(title="Carpeta Base para Terminal"):
            dlg = AdvancedItemDialog(self, title="Configurar Terminal", path_or_url=os.path.normpath(p), item_type="powershell")
            self.wait_window(dlg)
            if dlg.result:
                item_data = {"type": "powershell", "path": os.path.normpath(p)}
                item_data.update(dlg.result)
                self.apps_data[self.current_category].append(item_data)
                self._save_data()
                self.refresh_apps_list()

    def add_item(self, t, p, extras=None):
        item = {"type": t, "path": p}
        if extras:
            item.update(extras)
        self.apps_data[self.current_category].append(item)
        self._save_data()
        self.refresh_apps_list()
        
    def remove_item(self, idx):
        del self.apps_data[self.current_category][idx]
        self._save_data()
        self.refresh_apps_list()

    # --- LANZAMIENTO AVANZADO Y LIMPIEZA ---
    def open_clean_dialog(self):
        if not WINDOWS_LIBS_AVAILABLE:
            messagebox.showwarning("Error", "No se detectaron las librerías necesarias de Windows (win32gui, etc).")
            return
        dlg = CleanWorkspaceDialog(self)
        self.wait_window(dlg)

    def _wait_for_condition(self, condition_func, timeout=5.0, interval=0.1):
        import time
        start = time.time()
        while time.time() - start < timeout:
            res = condition_func()
            if res: return res
            time.sleep(interval)
        return None

    def _get_hwnds_for_pid(self, pid):
        import win32gui, win32process
        hwnds = []
        def callback(hwnd, _):
            if win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd):
                _, found_pid = win32process.GetWindowThreadProcessId(hwnd)
                if found_pid == pid:
                    hwnds.append(hwnd)
        win32gui.EnumWindows(callback, 0)
        return hwnds

    def apply_fz_layout_cli(self, layout_uuid, monitor_num=None):
        import os, subprocess
        possible = [
            r"C:\Program Files\PowerToys\FancyZonesCLI.exe",
            r"C:\Program Files\PowerToys\WinUI3Apps\FancyZonesCLI.exe",
        ]
        cli = next((p for p in possible if os.path.exists(p)), None)
        if not cli: return False

        cmd = [cli, "set-layout", layout_uuid]
        if monitor_num is None: cmd += ["--all"]
        else: cmd += ["--monitor", str(monitor_num)]

        try:
            subprocess.run(cmd, check=True, capture_output=True, text=True, creationflags=0x08000000)
            return True
        except Exception as e:
            print("FancyZonesCLI error:", e)
            return False

    def _ensure_required_virtual_desktops(self, items=None):
        """Comprueba cuántos escritorios virtuales se necesitan para el workspace actual
        y los crea si faltan."""
        if not WINDOWS_LIBS_AVAILABLE: return 0, ["Librerías de Windows no disponibles."]
        
        if items is None:
            items = self.apps_data.get(self.current_category, [])
        
        max_idx = 0 # 0 es 'Por defecto' (escritorio 1)
        for item in items:
            d_str = item.get('desktop', 'Por defecto')
            if d_str.startswith("Escritorio "):
                try:
                    idx = int(d_str.split(" ")[1]) - 1
                    if idx > max_idx: max_idx = idx
                except: pass
        
        created_count = 0
        errors = []
        try:
            from pyvda import get_virtual_desktops, VirtualDesktop
            current_desktops = get_virtual_desktops()
            num_needed = (max_idx + 1) - len(current_desktops)
            
            if num_needed > 0:
                for i in range(num_needed):
                    VirtualDesktop.create()
                    created_count += 1
                
                print(f"[Workspace Prep] Creados {created_count} escritorios virtuales.")
        except Exception as e:
            errors.append(f"No se pudieron crear los escritorios virtuales: {e}")
            
        return created_count, errors

    def _update_fz_warning(self):
        """Comprueba si hay discrepancias entre los layouts activos en FancyZones
        y los que espera la categoría actual. Muestra/oculta la barra de warning."""
        try:
            self.load_fancyzones_layouts()
            mismatches = self._check_fz_layout_mismatches()
            
            if mismatches:
                # Obtener nombres amigables de escritorios
                desk_names = {}
                if WINDOWS_LIBS_AVAILABLE:
                    try:
                        from pyvda import get_virtual_desktops
                        for i, d in enumerate(get_virtual_desktops()):
                            g = str(d.id).upper()
                            if not g.startswith("{"): g = "{" + g + "}"
                            desk_names[g] = d.name if d.name else f"Escritorio {i+1}"
                    except: pass
                
                lines = [f"⚠️ Hay {len(mismatches)} layout(s) o escritorio(s) que no coinciden:"]
                for m in mismatches:
                    # Formatear el nombre del escritorio
                    if m["desktop_guid"].startswith("MISSING_DESKTOP_"):
                        d_num = m["desktop_guid"].split("_")[-1]
                        desk = f"Escritorio {d_num}"
                    else:
                        desk = desk_names.get(m["desktop_guid"], m["desktop_guid"][:12] + "...")
                        
                    lines.append(f"  📺 {m['monitor']} / {desk}: activo=\"{m['actual_name']}\" ≠ esperado=\"{m['expected_name']}\"")
                
                self.fz_warning_label.configure(text="\n".join(lines))
                # Empaquetamos todo el contenedor exterior y el interior
                self.fz_warning_frame.pack(fill="x", padx=20, pady=(0, 5))
                self.fz_warning_inner.pack(fill="x", pady=2)
            else:
                # Ocultar warning completamente para que no quite espacio
                self.fz_warning_inner.pack_forget()
                self.fz_warning_frame.pack_forget()
        except Exception as e:
            print(f"[FZ Warning] Error comprobando layouts: {e}")
            self.fz_warning_inner.pack_forget()
            self.fz_warning_frame.pack_forget()

    def _on_sync_fz_click(self):
        """Maneja el click en el botón de sincronizar layouts de FancyZones."""
        self.btn_fz_sync.configure(state="disabled", text="⏳ Sincronizando...")
        
        def _task():
            # 1. Asegurar escritorios virtuales necesarios
            created_vds, vd_errors = self._ensure_required_virtual_desktops()
            
            # 2. Sincronizar layouts (necesita que los GUIDs de los nuevos escritorios ya existan)
            # Pequeña espera para que Windows registre los nuevos GUIDs
            if created_vds > 0: time.sleep(1.0) 
            
            count, errors = self._sync_fz_layouts_for_workspace()
            errors.extend(vd_errors)
            
            def _done():
                self.btn_fz_sync.configure(state="normal", text="🔄 Sincronizar Layouts")
                
                msg = ""
                if created_vds > 0:
                    msg += f"✅ Se han creado {created_vds} escritorio(s) virtual(es) nuevo(s).\n"
                
                if count > 0:
                    msg += f"✅ Se han sincronizado {count} layout(s) en FancyZones.\n\n"
                    msg += "Los cambios de zona se aplicarán la próxima vez que muevas una ventana con Shift "
                    msg += "o al reiniciar FancyZones (Win+Shift+`)."
                
                if not msg and not errors:
                    msg = "Todo OK: Los escritorios y layouts ya estaban correctamente sincronizados."
                
                if errors:
                    msg += "\n\nAdvertencias:\n" + "\n".join(errors)
                
                if msg:
                    messagebox.showinfo("Sincronización Completada", msg, parent=self)
                
                # Actualizar la barra de warning
                self._update_fz_warning()
            
            self.after(0, _done)
        
        import threading
        threading.Thread(target=_task, daemon=True).start()

    def _get_workspace_required_layouts(self, items_to_launch=None):
        """
        Analiza los items de la categoría actual para deducir qué layout necesita cada 
        combinación (fz_monitor_id, vd_guid). Devuelve un dict:
           {(fz_monitor_id, vd_guid): {"layout_name": str, "layout_uuid": str}}
        """
        if items_to_launch is None:
            items_to_launch = self.apps_data.get(self.current_category, [])
        
        desk_guids = []
        if WINDOWS_LIBS_AVAILABLE:
            try:
                from pyvda import get_virtual_desktops
                desk_guids = [d.id for d in get_virtual_desktops()]
            except: pass

        required = {}  # {(fz_monitor_id, vd_guid): {"layout_name": str, "layout_uuid": str}}

        for item in items_to_launch:
            zone_name = item.get('fancyzone', 'Ninguna')
            if zone_name == 'Ninguna':
                continue

            parts = zone_name.rsplit(" - Zona ", 1)
            if len(parts) < 2:
                continue
            layout_name = parts[0]
            layout_uuid = item.get("fancyzone_uuid", "")

            # Si el item no tiene UUID guardado (config vieja), o si ha cambiado, buscarlo
            layout_info = None
            if layout_uuid:
                # Intentar buscar por UUID en el sistema actual
                layout_info = next((dt for n, dt in self.available_layouts.items() 
                                    if str(dt.get("uuid", "")).strip("{}").lower() == layout_uuid.strip("{}").lower()), None)
            
            # Si no lo encontramos por UUID, buscar por nombre (fallback)
            if not layout_info:
                layout_info = self.available_layouts.get(layout_name)
                if layout_info:
                    layout_uuid = layout_info.get("uuid", "")

            if not layout_info:
                # Intentar buscar en el caché persistente (portabilidad)
                if layout_uuid and layout_uuid in self.fz_layouts_cache:
                    layout_info = self.fz_layouts_cache[layout_uuid].get("info", {})
                elif layout_name:
                    # Fallback desesperado: buscar en el caché por nombre si el UUID no coincide
                    for luuid, ldef in self.fz_layouts_cache.items():
                        if ldef.get("name") == layout_name:
                            layout_info = ldef.get("info", {})
                            layout_uuid = luuid
                            break
            
            if not layout_info or not layout_uuid:
                continue

            # Escritorio virtual
            desktop = item.get('desktop', 'Por defecto')
            vd_guid = None
            if desktop.startswith("Escritorio "):
                try:
                    d_idx = int(desktop.split(" ")[1]) - 1
                    if 0 <= d_idx < len(desk_guids):
                        vd_guid = str(desk_guids[d_idx]).upper()
                        if not vd_guid.startswith("{"): vd_guid = "{" + vd_guid + "}"
                    else:
                        # Si el escritorio no existe, marcamos una ID especial para avisar
                        vd_guid = f"MISSING_DESKTOP_{d_idx+1}"
                except:
                    pass

            # Monitor (extraer ID hardware)
            mon = item.get('monitor', 'Por defecto')
            fz_monitor_id = None
            
            # --- MEJORA: Búsqueda inteligente de monitor ---
            # 1. Intentar por ID exacto
            if "[" in mon and "]" in mon:
                hw_id_in_config = mon.split("[")[1].split("]")[0]
                
                # Comprobar si este ID existe en el sistema actual
                found_id = None
                active_fz_monitors = []
                if hasattr(self, 'get_active_fz_monitors'): # Si estamos en el diálogo
                     active_fz_monitors = self.get_active_fz_monitors()
                else: 
                     # Fallback: Extraer IDs de available_monitors que se cargó en el init
                     for am in self.available_monitors:
                         if "[" in am and "]" in am:
                             active_fz_monitors.append(am.split("[")[1].split("]")[0])

                if hw_id_in_config in active_fz_monitors:
                    fz_monitor_id = hw_id_in_config
                else:
                    # 2. Si no coincide el ID exacto, intentar por índice de pantalla (P. ej: "Pantalla 2")
                    prefix = mon.split("[")[0].strip() # "Pantalla 2"
                    for am in self.available_monitors:
                        if am.startswith(prefix):
                            if "[" in am and "]" in am:
                                fz_monitor_id = am.split("[")[1].split("]")[0]
                                print(f"[FZ Sync] Adaptando monitor: {mon} -> Usando ID actual {fz_monitor_id}")
                                break
                
                # Fallback final si es el monitor principal
                if not fz_monitor_id and "Display Principal" in mon:
                    fz_monitor_id = "LOCALDISPLAY" # O intentar encontrar qué monitor es el principal

            if not fz_monitor_id or not vd_guid:
                continue

            key = (fz_monitor_id, vd_guid)
            if key not in required:
                required[key] = {"layout_name": layout_name, "layout_uuid": layout_uuid}

        return required

    def _check_fz_layout_mismatches(self, items_to_launch=None):
        """
        Compara los layouts requeridos por la categoría actual con los que están activos
        en applied-layouts.json de PowerToys.
        """
        import os, json
        
        required = self._get_workspace_required_layouts(items_to_launch)
        if not required:
            return []

        applied_path = os.path.join(self.fancyzones_path, "applied-layouts.json")
        if not os.path.exists(applied_path):
            return []

        try:
            with open(applied_path, 'r', encoding='utf-8') as f:
                applied_data = json.load(f)
        except:
            return []

        layouts_list = applied_data.get("applied-layouts", [])
        mismatches = []

        for (req_mon, req_vd), info in required.items():
            # Caso especial: El escritorio no existe en el sistema
            if req_vd.startswith("MISSING_DESKTOP_"):
                d_num = req_vd.split("_")[-1]
                mismatches.append({
                    "monitor": req_mon,
                    "desktop_guid": req_vd,
                    "expected_name": info["layout_name"],
                    "actual_name": f"❌ ¡Escritorio {d_num} NO EXISTE!",
                    "expected_uuid": info["layout_uuid"],
                    "actual_uuid": "",
                })
                continue

            expected_uuid = str(info["layout_uuid"]).strip("{}").lower()
            found = False

            for entry in layouts_list:
                dev = entry.get("device", {})
                entry_mon = dev.get("monitor", "")
                entry_vd = dev.get("virtual-desktop", "").upper()
                if not entry_vd.startswith("{"): entry_vd = "{" + entry_vd + "}"

                if entry_mon == req_mon and entry_vd == req_vd:
                    found = True
                    actual_uuid = str(entry.get("applied-layout", {}).get("uuid", "")).strip("{}").lower()
                    if actual_uuid != expected_uuid:
                        # Buscar nombre del layout actual
                        actual_name = next((n for n, dt in self.available_layouts.items()
                                          if str(dt.get("uuid", "")).strip("{}").lower() == actual_uuid), 
                                         f"Desconocido ({actual_uuid[:8]}...)")
                        mismatches.append({
                            "monitor": req_mon,
                            "desktop_guid": req_vd,
                            "expected_name": info["layout_name"],
                            "actual_name": actual_name,
                            "expected_uuid": expected_uuid,
                            "actual_uuid": actual_uuid,
                        })
                    break

            if not found:
                mismatches.append({
                    "monitor": req_mon,
                    "desktop_guid": req_vd,
                    "expected_name": info["layout_name"],
                    "actual_name": "Sin asignar (no existe entrada en FancyZones)",
                    "expected_uuid": expected_uuid,
                    "actual_uuid": "",
                })

        return mismatches

    def _sync_fz_layouts_for_workspace(self, items_to_launch=None):
        """
        Sincroniza los layouts de FancyZones para que coincidan con lo que espera
        la categoría actual.
        """
        import os, json

        required = self._get_workspace_required_layouts(items_to_launch)
        if not required:
            return 0, ["No hay layouts de FancyZones configurados en esta categoría."]

        applied_path = os.path.join(self.fancyzones_path, "applied-layouts.json")
        if not os.path.exists(applied_path):
            return 0, ["No se encontró applied-layouts.json de PowerToys."]

        try:
            with open(applied_path, 'r', encoding='utf-8') as f:
                applied_data = json.load(f)
        except Exception as e:
            return 0, [f"Error leyendo applied-layouts.json: {e}"]

        layouts_list = applied_data.get("applied-layouts", [])
        modified_count = 0
        new_layouts_created = 0
        errors = []

        for (req_mon, req_vd), info in required.items():
            layout_name = info["layout_name"]
            expected_uuid = str(info["layout_uuid"]).strip("{}").lower()
            
            # --- MEJORA: Asegurar que el layout existe en PowerToys ---
            # Comprobamos si el UUID ya existe en el sistema actual
            layout_exists_locally = any(str(dt.get("uuid", "")).strip("{}").lower() == expected_uuid 
                                        for dt in self.available_layouts.values())
            
            if not layout_exists_locally:
                # El layout no existe localmente, pero quizá lo tenemos en el caché por UUID
                inject_def = self.fz_layouts_cache.get("{" + expected_uuid.upper() + "}")
                if not inject_def:
                    inject_def = self.fz_layouts_cache.get(expected_uuid)
                
                # Si no está por UUID, intentar por nombre como último recurso
                if not inject_def:
                    for luuid, ldef in self.fz_layouts_cache.items():
                        if ldef.get("name") == layout_name:
                            inject_def = ldef
                            break
                
                if inject_def:
                    success = self._inject_layout_to_powertoys(inject_def)
                    if success:
                        new_layouts_created += 1
                        print(f"[FZ Sync] Layout '{layout_name}' ({expected_uuid}) recreado desde caché.")
                        self.load_fancyzones_layouts()
                    else:
                        errors.append(f"❌ No se pudo crear el layout '{layout_name}' en PowerToys.")
                        continue
                else:
                    errors.append(f"❌ Layout '{layout_name}' no encontrado ni en el sistema ni en el caché.")
                    continue

            found = False

            for entry in layouts_list:
                dev = entry.get("device", {})
                entry_mon = dev.get("monitor", "")
                entry_vd = dev.get("virtual-desktop", "").upper()
                if not entry_vd.startswith("{"): entry_vd = "{" + entry_vd + "}"

                if entry_mon == req_mon and entry_vd == req_vd:
                    found = True
                    app_lay = entry.get("applied-layout", {})
                    current_uuid = str(app_lay.get("uuid", "")).strip("{}").lower()

                    if current_uuid != expected_uuid:
                        app_lay["uuid"] = "{" + expected_uuid.upper() + "}"
                        app_lay["type"] = "custom"
                        modified_count += 1
                        print(f"[FZ Sync] Cambiado: {req_mon}/{req_vd[:12]}... "
                              f"UUID {current_uuid[:8]}... -> {expected_uuid[:8]}... ({info['layout_name']})")
                    break

            if not found:
                # Intentar clonar una entrada para MISMO MONITOR pero OTRO ESCRITORIO
                ref_entry = None
                for entry in layouts_list:
                    if entry.get("device", {}).get("monitor") == req_mon:
                        ref_entry = entry
                        break
                
                if ref_entry:
                    import copy
                    new_entry = copy.deepcopy(ref_entry)
                    # Guardar con el GUID del nuevo escritorio (en minúsculas según estándar FZ)
                    new_entry["device"]["virtual-desktop"] = req_vd.lower().strip("{}")
                    new_entry["applied-layout"]["uuid"] = "{" + expected_uuid.upper() + "}"
                    new_entry["applied-layout"]["type"] = "custom"
                    layouts_list.append(new_entry)
                    modified_count += 1
                    print(f"[FZ Sync] CREADO (CLON): {req_mon}/{req_vd[:12]}... -> {expected_uuid[:8]}...")
                else:
                    errors.append(f"⚠️ No existe entrada previa para {req_mon} en applied-layouts.json. "
                                f"Interactúa con FancyZones en este monitor al menos una vez para que PowerToys lo registre.")

        if modified_count > 0:
            try:
                with open(applied_path, 'w', encoding='utf-8') as f:
                    json.dump(applied_data, f, indent=2)
                print(f"[FZ Sync] applied-layouts.json guardado. {modified_count} entradas cambiadas.")
            except Exception as e:
                return 0, [f"Error escribiendo applied-layouts.json: {e}"]

        return modified_count + new_layouts_created, errors

    def _inject_layout_to_powertoys(self, layout_def):
        """Inyecta una definición de layout desde el caché al archivo custom-layouts.json de PowerToys."""
        layout_name = layout_def.get("name", "Unnamed")
        layout_uuid = str(layout_def.get("uuid", "")).strip("{}").upper()
        
        custom_path = os.path.join(self.fancyzones_path, "custom-layouts.json")
        try:
            data = {"custom-layouts": []}
            if os.path.exists(custom_path):
                with open(custom_path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
            
            # Evitar duplicados por UUID (que es el ID real)
            for l in data.get("custom-layouts", []):
                curr_u = str(l.get("uuid", "")).strip("{}").upper()
                if curr_u == layout_uuid:
                    return True
                
            # Si el UUID es nuevo pero el nombre ya existe, modificamos el nombre para evitar confusión
            name_exists = any(l.get("name") == layout_name for l in data.get("custom-layouts", []))
            if name_exists:
                layout_def = layout_def.copy()
                layout_def["name"] = f"{layout_name} (Sincronizado)"
            
            data.setdefault("custom-layouts", []).append(layout_def)
            
            with open(custom_path, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=4)
            
            return True
        except Exception as e:
            print(f"Error inyectando layout: {e}")
            return False

    def _force_foreground(self, hwnd):
        import win32gui, win32con, win32process, win32api, ctypes
        if not hwnd or not win32gui.IsWindow(hwnd): return False
        
        try:
            if win32gui.IsIconic(hwnd): win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        except: pass

        try:
            # SwitchToThisWindow es el método más agresivo en Windows modernos
            ctypes.windll.user32.SwitchToThisWindow(hwnd, True)
            
            # Truco del Alt para desbloquear SetForegroundWindow
            ctypes.windll.user32.keybd_event(0x12, 0, 0, 0)      # Alt Down
            ctypes.windll.user32.keybd_event(0x12, 0, 0x0002, 0)  # Alt Up
            
            win32gui.SetForegroundWindow(hwnd)
            win32gui.BringWindowToTop(hwnd)
            
            # Forzar posición Z al frente absoluto sin tamaño
            flags = win32con.SWP_NOMOVE | win32con.SWP_NOSIZE | win32con.SWP_SHOWWINDOW
            win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, 0, 0, flags)
            win32gui.SetWindowPos(hwnd, win32con.HWND_NOTOPMOST, 0, 0, 0, 0, flags)

            return True
        except:
            return False

    def _start_global_hotkeys(self):
        import threading
        
        def run_listeners():
            try:
                from pynput import keyboard, mouse
                
                # --- Mapas de acciones de Teclado ---
                # Usaremos la forma de pynput para registrar hotkeys. Las teclas modificadoras usan `<win>` o `<ctrl>`
                # y las letras minúsculas (ej: `l`, `r`, `page_down`).
                def normalize_kb_str(kstr):
                    return kstr.replace("pagedown", "page_down").replace("pageup", "page_up")
                    
                def format_pynput_str(kstr):
                    parts = kstr.split('+')
                    res = []
                    for p in parts:
                        p = normalize_kb_str(p).lower()
                        if p == 'win': p = 'cmd'
                        
                        structural_keys = ['ctrl', 'alt', 'shift', 'cmd', 'page_down', 'page_up', 
                                           'home', 'end', 'up', 'down', 'left', 'right', 'enter', 
                                           'space', 'esc', 'tab', 'backspace', 'delete', 'insert']
                        if p in structural_keys:
                            res.append(f"<{p}>")
                        else:
                            res.append(p)
                    return "+".join(res)
                    
                def is_mouse_combo(kstr):
                    parts = kstr.lower().split('+')
                    mouse_btns = ['x1', 'x2', 'mouse_left', 'mouse_right', 'mouse_middle']
                    return any(b in parts for b in mouse_btns)

                kb_mapping = {}
                hk = self.hotkeys_data
                
                # Flags de activación
                zone_cycle_enabled = hk.get("_zone_cycle_enabled", True)
                desktop_cycle_enabled = hk.get("_desktop_cycle_enabled", True)
                print(f"[Hotkeys] Zone Cycle: {'ON' if zone_cycle_enabled else 'OFF'}, Desktop Cycle: {'ON' if desktop_cycle_enabled else 'OFF'}")
                
                # Acciones a considerar (filtradas por flags de activación)
                actions = {}
                if zone_cycle_enabled:
                    actions["mouse_cycle_fwd"] = self._cycle_zone_forward
                    actions["mouse_cycle_bwd"] = self._cycle_zone_backward
                    actions["cycle_forward"] = self._cycle_zone_forward
                    actions["cycle_backward"] = self._cycle_zone_backward
                if desktop_cycle_enabled:
                    actions["desktop_cycle_fwd"] = self._cycle_desktop_forward
                    actions["desktop_cycle_bwd"] = self._cycle_desktop_backward
                actions["util_reload_layouts"] = self.load_fancyzones_layouts

                # Registrar solo teclado puro en GlobalHotKeys
                for key_id, func in actions.items():
                    combo = hk.get(key_id)
                    if not combo or combo.lower() == 'ninguno':
                        print(f"[Hotkeys] IGNORADO (sin combo): '{key_id}' = '{combo}'")
                        continue
                    if is_mouse_combo(combo):
                        print(f"[Hotkeys] RATON registrado: '{key_id}' = '{combo}'")
                    else:
                        formatted = format_pynput_str(combo)
                        kb_mapping[formatted] = func
                        print(f"[Hotkeys] TECLADO registrado: '{key_id}' = '{combo}' -> pynput='{formatted}'")

                # --- Lógica de Estado para Combinaciones Teclado+Ratón ---
                kb_state = {"ctrl": False, "alt": False, "shift": False, "win": False}
                
                def on_press(key):
                    if hasattr(key, 'name'):
                        nm = key.name
                        if nm in ['ctrl_l', 'ctrl_r']: kb_state['ctrl'] = True
                        elif nm in ['alt_l', 'alt_r', 'alt_gr']: kb_state['alt'] = True
                        elif nm in ['shift', 'shift_r']: kb_state['shift'] = True
                        elif nm in ['cmd', 'cmd_r', 'win']: kb_state['win'] = True
                    return True
                    
                def on_release(key):
                    if hasattr(key, 'name'):
                        nm = key.name
                        if nm in ['ctrl_l', 'ctrl_r']: kb_state['ctrl'] = False
                        elif nm in ['alt_l', 'alt_r', 'alt_gr']: kb_state['alt'] = False
                        elif nm in ['shift', 'shift_r']: kb_state['shift'] = False
                        elif nm in ['cmd', 'cmd_r', 'win']: kb_state['win'] = False
                    return True

                def match_mouse_hotkey(macro_str, btn_str):
                    if not macro_str or macro_str == 'Ninguno': return False
                    parts = set(macro_str.lower().split('+'))
                    
                    check_btn = btn_str if btn_str in ['x1', 'x2'] else f"mouse_{btn_str}"
                    
                    # Verificar si el botón está en la macro
                    if check_btn not in parts: return False
                    
                    # Verificar modificadores requeridos
                    needs_ctrl = "ctrl" in parts
                    needs_alt = "alt" in parts
                    needs_shift = "shift" in parts
                    needs_win = "win" in parts or "cmd" in parts
                    
                    # Usar comprobación síncrona real del hardware (GetAsyncKeyState)
                    import win32api
                    real_alt = (win32api.GetAsyncKeyState(0x12) & 0x8000) != 0
                    real_ctrl = (win32api.GetAsyncKeyState(0x11) & 0x8000) != 0
                    real_shift = (win32api.GetAsyncKeyState(0x10) & 0x8000) != 0
                    real_win = (win32api.GetAsyncKeyState(0x5B) & 0x8000) != 0 or (win32api.GetAsyncKeyState(0x5C) & 0x8000) != 0

                    is_alt = real_alt or kb_state['alt']
                    is_ctrl = real_ctrl or kb_state['ctrl']
                    is_shift = real_shift or kb_state['shift']
                    is_win = real_win or kb_state['win']
                    
                    # Comparación ESTRICTA: las teclas reales deben coincidir EXACTAMENTE con las requeridas.
                    # Esto evita que "x1" (sin modificadores) se dispare cuando hay Alt pulsado.
                    return (is_ctrl == needs_ctrl and is_alt == needs_alt and
                            is_shift == needs_shift and is_win == needs_win)

                suppressed_mouse_btns = set()

                # --- Integración con HookManager (X1/X2 Supresión Total) ---
                def handle_x_down(btn):
                    # ¿Hay conflicto simple/complejo?
                    mouse_combos = []
                    for key_id, func in actions.items():
                        combo = hk.get(key_id)
                        if combo and is_mouse_combo(combo):
                            if btn in combo.lower().split('+'):
                                mouse_combos.append((key_id, func, combo))
                    
                    if not mouse_combos: return

                    has_complex = any(len(c[2].split('+')) > 1 for c in mouse_combos)
                    has_simple = any(len(c[2].split('+')) == 1 for c in mouse_combos)
                    
                    if has_complex and has_simple:
                        time.sleep(0.04) # Breve espera para modificadores
                    
                    candidates = []
                    for key_id, func, combo in mouse_combos:
                        if match_mouse_hotkey(combo, btn):
                            candidates.append((len(combo.split('+')), func))
                    
                    if candidates:
                        candidates.sort(key=lambda c: c[0], reverse=True)
                        candidates[0][1]()

                if hasattr(self, 'hook_manager'):
                    self.hook_manager.on_x1_down = lambda: handle_x_down('x1')
                    self.hook_manager.on_x2_down = lambda: handle_x_down('x2')

                    def check_x_mapped_func(btn, alt, ctrl, shift):
                        for key_id, func in actions.items():
                            combo = hk.get(key_id)
                            if combo and is_mouse_combo(combo):
                                parts = set(combo.lower().split('+'))
                                check_btn = btn if btn in ['x1', 'x2'] else f"mouse_{btn}"
                                if check_btn in parts:
                                    needs_alt = "alt" in parts
                                    needs_ctrl = "ctrl" in parts
                                    needs_shift = "shift" in parts
                                    if needs_alt == alt and needs_ctrl == ctrl and needs_shift == shift:
                                        return True
                        return False

                    self.hook_manager.check_x_mapped = check_x_mapped_func

                def win32_event_filter(msg, data):
                    is_down = msg in (0x0201, 0x0204, 0x0207, 0x020B)
                    is_up = msg in (0x0202, 0x0205, 0x0208, 0x020C)
                    if not (is_down or is_up):
                        return True
                        
                    btn_name = None
                    if msg in (0x0201, 0x0202): btn_name = 'left'
                    elif msg in (0x0204, 0x0205): btn_name = 'right'
                    elif msg in (0x0207, 0x0208): btn_name = 'middle'
                    elif msg in (0x020B, 0x020C):
                        # X1/X2 AHORA SON MANEJADOS POR GlobalHookManager
                        # Devolvemos True para que Pynput ni se entere, ya que HookManager ya suprimió el evento
                        return True

                    if not btn_name:
                        return True

                    # UP de un botón que suprimimos en DOWN → suprimir también
                    if is_up and btn_name in suppressed_mouse_btns:
                        suppressed_mouse_btns.discard(btn_name)
                        return False

                    if not is_down:
                        return True


                def on_click(x, y, button, pressed):
                    if pressed:
                        btn_name = getattr(button, 'name', str(button))
                        if btn_name in ['x1', 'x2']:
                            return True # Ignorar aquí, gestionado por HookManager
                        
                        candidates = []
                        for key_id, func in actions.items():
                            combo = hk.get(key_id)
                            if combo and is_mouse_combo(combo):
                                if match_mouse_hotkey(combo, btn_name):
                                    specificity = len(combo.split('+'))
                                    candidates.append((specificity, key_id, func))
                        if candidates:
                            candidates.sort(key=lambda c: c[0], reverse=True)
                            _, _, best_func = candidates[0]
                            best_func()
                    else: # RELEASE
                        # Cuando soltamos el clic izquierdo con Shift, dejamos que FancyZones haga su 
                        # encaje visual real. Luego, pasado un momento, actualizamos su grupo.
                        import win32api
                        real_shift = (win32api.GetAsyncKeyState(0x10) & 0x8000) != 0
                        is_shift_down = kb_state.get('shift') or real_shift
                        if getattr(button, 'name', '') == 'left' and is_shift_down:
                            import threading
                            def _delayed_zone_update():
                                import time, win32gui
                                print("[Snap] Shift+LClick suelto - esperando encaje de FancyZones...")
                                # Intentar detectar la zona varias veces por si la animación de FZ es lenta
                                for attempt in range(5):
                                    time.sleep(0.4) 
                                    hwnd = win32gui.GetForegroundWindow()
                                    if not hwnd or not win32gui.IsWindow(hwnd): break
                                    
                                    target_key = self._detect_zone_for_window(hwnd)
                                    print(f"[Snap] Intento {attempt+1}: hwnd={hwnd} target_key={target_key}")
                                    if target_key:
                                        # Buscar grupo EXISTENTE que coincida en MONITOR + ZONA
                                        # El layout UUID puede diferir entre config de usuario y applied-layouts de FZ
                                        detected_desktop = target_key[0] # virtual desktop
                                        detected_device = target_key[1]  # monitor device
                                        detected_z_idx = target_key[3]   # zone index
                                        
                                        # Buscar grupo existente en el mismo escritorio, monitor y misma zona
                                        existing_key = None
                                        for k in self.zone_stacks:
                                            if len(k) >= 4 and k[0] == detected_desktop and k[1] == detected_device and k[3] == detected_z_idx:
                                                existing_key = k
                                                break
                                        
                                        # Si hay grupo existente, usar esa clave; si no, usar la detectada
                                        final_key = existing_key if existing_key else target_key
                                        
                                        # Quitar de otros stacks
                                        for k in list(self.zone_stacks.keys()):
                                            if hwnd in self.zone_stacks[k] and k != final_key:
                                                self.zone_stacks[k].remove(hwnd)
                                                print(f"[Snap] Eliminada del grupo anterior: {k}")
                                                
                                        # Unirse al stack de esa zona
                                        if final_key not in self.zone_stacks: 
                                            self.zone_stacks[final_key] = []
                                        if hwnd not in self.zone_stacks[final_key]:
                                            self.zone_stacks[final_key].append(hwnd)
                                        print(f"[Snap OK] Ventana {hwnd} unida a grupo: {final_key}")
                                        print(f"[Snap OK] Grupo completo ({len(self.zone_stacks[final_key])}): {self.zone_stacks[final_key]}")
                                        break
                            threading.Thread(target=_delayed_zone_update, daemon=True).start()
                    return True

                # Iniciar listenes de Pynput
                h = keyboard.GlobalHotKeys(kb_mapping)
                h.start()
                
                # Usar win32_event_filter intercepta y bloquea la pulsación para que Windows no la procese (ej: atrás en navegador)
                ml = mouse.Listener(on_click=on_click, win32_event_filter=win32_event_filter)
                ml.start()
                
                kl = keyboard.Listener(on_press=on_press, on_release=on_release)
                kl.start()

                self.hotkeys_active = True
                h.join()
            except Exception as e:
                print(f"Error iniciando Hotkeys Pynput: {e}")
        
        t = threading.Thread(target=run_listeners, daemon=True)
        t.start()
        
    def _get_active_zone_context(self):
        import win32gui, win32api, win32con
        fg_hwnd = win32gui.GetForegroundWindow()
        
        pos = win32api.GetCursorPos()
        try:
            mouse_hwnd = win32gui.WindowFromPoint(pos)
        except:
            mouse_hwnd = None
            
        # Priorizar la ventana bajo el ratón sobre la foreground
        # Así el ciclo funciona con un solo clic aunque la zona no tenga el foco
        candidates = []
        if mouse_hwnd: candidates.append(mouse_hwnd)
        if fg_hwnd and fg_hwnd != mouse_hwnd: candidates.append(fg_hwnd)
        
        check_list = []
        for c in candidates:
            if c:
                check_list.append(c)
                try:
                    root = win32gui.GetAncestor(c, win32con.GA_ROOTOWNER)
                    if root and root not in check_list: check_list.append(root)
                except:
                    pass
        
        target_key = None
        found_target_hwnd = None
        
        # BUSQUEDA ESTATICA: Solo miramos en que grupo ESTÁ registrada la ventana.
        for h in check_list:
            for key, stack in self.zone_stacks.items():
                if h in stack:
                    target_key = key
                    found_target_hwnd = h
                    break
            if target_key: break
            
        if not target_key: return fg_hwnd, None, None
        
        # Purgar solo ventanas muertas (que ya no existen en el SO)
        valid_stack = [h for h in self.zone_stacks[target_key] if win32gui.IsWindow(h)]
        self.zone_stacks[target_key] = valid_stack
        
        # Si la foreground está en el mismo stack, usarla como referencia para el ciclo
        # (así el avance es relativo a la ventana que realmente se ve)
        ref_hwnd = found_target_hwnd
        if fg_hwnd in valid_stack:
            ref_hwnd = fg_hwnd
        
        return ref_hwnd, target_key, valid_stack

    def _cycle_zone_forward(self):
        import threading, time
        if not hasattr(self, '_cycle_lock'): self._cycle_lock = threading.Lock()
        def task():
            with self._cycle_lock:
                try:
                    fg, key, stack = self._get_active_zone_context()
                    print(f"[Cycle FWD] fg={fg} key={key} stack={stack}")
                    now = time.time()
                    if hasattr(self, '_last_cycle_time') and (now - self._last_cycle_time) < 0.5:
                        if hasattr(self, '_last_cycle_hwnd') and stack and self._last_cycle_hwnd in stack:
                            fg = self._last_cycle_hwnd

                    if stack and len(stack) > 1:
                        if fg in stack:
                            idx = stack.index(fg)
                            next_idx = (idx + 1) % len(stack)
                        else:
                            next_idx = 0
                        target_hwnd = stack[next_idx]
                        print(f"[Cycle FWD] Activando hwnd={target_hwnd}")
                        self._force_foreground(target_hwnd)
                        self._last_cycle_hwnd = target_hwnd
                        self._last_cycle_time = time.time()
                    else:
                        print(f"[Cycle FWD] Sin stack o stack de 1 elemento, nada que rotar.")
                except Exception as e:
                    print(f"Error en ciclo forward: {e}")
        threading.Thread(target=task, daemon=True).start()

    def _cycle_zone_backward(self):
        import threading, time
        if not hasattr(self, '_cycle_lock'): self._cycle_lock = threading.Lock()
        def task():
            with self._cycle_lock:
                try:
                    fg, key, stack = self._get_active_zone_context()
                    print(f"[Cycle BWD] fg={fg} key={key} stack={stack}")
                    now = time.time()
                    if hasattr(self, '_last_cycle_time') and (now - self._last_cycle_time) < 0.5:
                        if hasattr(self, '_last_cycle_hwnd') and stack and self._last_cycle_hwnd in stack:
                            fg = self._last_cycle_hwnd

                    if stack and len(stack) > 1:
                        if fg in stack:
                            idx = stack.index(fg)
                            next_idx = (idx - 1) % len(stack)
                        else:
                            next_idx = len(stack) - 1
                        target_hwnd = stack[next_idx]
                        print(f"[Cycle BWD] Activando hwnd={target_hwnd}")
                        self._force_foreground(target_hwnd)
                        self._last_cycle_hwnd = target_hwnd
                        self._last_cycle_time = time.time()
                    else:
                        print(f"[Cycle BWD] Sin stack o stack de 1 elemento, nada que rotar.")
                except Exception as e:
                    print(f"Error en ciclo backward: {e}")
        threading.Thread(target=task, daemon=True).start()

    def _cycle_desktop_forward(self):
        import threading
        def task():
            if not WINDOWS_LIBS_AVAILABLE: return
            try:
                from pyvda import get_virtual_desktops, VirtualDesktop
                desktops = get_virtual_desktops()
                if not desktops: return
                current = VirtualDesktop.current()
                idx = -1
                for i, d in enumerate(desktops):
                    if d.id == current.id:
                        idx = i
                        break
                if idx != -1:
                    next_idx = (idx + 1) % len(desktops)
                    desktops[next_idx].go()
            except Exception as e:
                print(f"Error ciclo escritorio fwd: {e}")
        threading.Thread(target=task, daemon=True).start()
        
    def _cycle_desktop_backward(self):
        import threading
        def task():
            if not WINDOWS_LIBS_AVAILABLE: return
            try:
                from pyvda import get_virtual_desktops, VirtualDesktop
                desktops = get_virtual_desktops()
                if not desktops: return
                current = VirtualDesktop.current()
                idx = -1
                for i, d in enumerate(desktops):
                    if d.id == current.id:
                        idx = i
                        break
                if idx != -1:
                    next_idx = (idx - 1) % len(desktops)
                    desktops[next_idx].go()
            except Exception as e:
                print(f"Error ciclo escritorio bwd: {e}")
        threading.Thread(target=task, daemon=True).start()
            
    def _focus_zone_first(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 0: self._force_foreground(stack[0])

    def _focus_zone_last(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 0: self._force_foreground(stack[-1])
        
    def _move_to_stack_bottom(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 1:
            stack.remove(fg)
            stack.append(fg)
            self.zone_stacks[key] = stack
            
    def _move_to_stack_top(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 1:
            stack.remove(fg)
            stack.insert(0, fg)
            self.zone_stacks[key] = stack
            
    def _rebuild_dangling_stacks(self):
        print("Rebuild Stacks (Ctrl+Alt+R) ejecutado: Stacks purgados.")
        for k in self.zone_stacks:
            import win32gui
            self.zone_stacks[k] = [h for h in self.zone_stacks[k] if win32gui.IsWindow(h)]

    def recover_workspace(self):
        import win32gui, win32process, win32api, win32con, copy, threading, os
        
        # Load layouts directly before detecting anything
        self.load_fancyzones_layouts()
        self.btn_recover.configure(state="disabled")
        
        items_to_launch = self.apps_data.get(self.current_category, [])
        if not items_to_launch:
            self.btn_recover.configure(state="normal")
            return
            
        def _extract_domain_kw(url_str):
            d = url_str.replace("https://", "").replace("http://", "").split("/")[0]
            d = d.replace("www.", "")
            return d.split(".")[0] if "." in d else d

        def _task():
            try:
                # 1. Recopilar estado real (Monitores, Escritorios)
                desk_guids = []
                if WINDOWS_LIBS_AVAILABLE:
                    try:
                        from pyvda import get_virtual_desktops, VirtualDesktop
                        desktops = get_virtual_desktops()
                        desk_guids = [d.id for d in desktops]
                    except: pass
                    
                active_fz_mons = {}
                if WINDOWS_LIBS_AVAILABLE:
                    try:
                        i = 0
                        while True:
                            d = win32api.EnumDisplayDevices(None, i, 0)
                            if not d.DeviceName: break
                            if d.StateFlags & 1:
                                m_i = 0
                                while True:
                                    try:
                                        m = win32api.EnumDisplayDevices(d.DeviceName, m_i, 0)
                                        if not m.DeviceID: break
                                        parts = m.DeviceID.split("\\")
                                        if len(parts) > 1 and parts[0] == "MONITOR":
                                            active_fz_mons[parts[1]] = d.DeviceName
                                        m_i += 1
                                    except: break
                            i += 1
                    except Exception: pass

                monitors_info = []
                try:
                    for idx, (hMonitor, _, pyRect) in enumerate(win32api.EnumDisplayMonitors()):
                        minfo = win32api.GetMonitorInfo(hMonitor)
                        monitors_info.append({
                            "device": minfo.get("Device", f"\\\\.\\DISPLAY{idx+1}"),
                            "work_area": minfo.get("Work"),
                            "bounds": pyRect,
                            "enum_idx": idx,
                            "is_primary": minfo.get("Flags", 0) == 1
                        })
                except: pass

                def _norm(gid):
                    if not gid: return "00000000-0000-0000-0000-000000000000"
                    return str(gid).strip("{}").lower()

                # 2. Generar intents precalculados (igual que al lanzar, pero sin ejecutar proc)
                unmatched_intents = []
                for item in items_to_launch:
                    desktop = item.get('desktop', 'Por defecto')
                    mon = item.get('monitor', 'Por defecto')
                    
                    d_guid = None
                    if desktop.startswith("Escritorio "):
                        try: 
                            d_idx = int(desktop.split(" ")[1]) - 1
                            if 0 <= d_idx < len(desk_guids): d_guid = desk_guids[d_idx]
                        except: pass
                    
                    if d_guid is None:
                        try:
                            from pyvda import VirtualDesktop
                            d_guid = VirtualDesktop.current().id
                        except: pass
                        
                    m_dev = monitors_info[0]["device"] if monitors_info else "\\\\.\\DISPLAY1"
                    m_eidx = 0
                    
                    target_dev = None
                    if "[" in mon and "]" in mon:
                        hw_id = mon.split("[")[1].split("]")[0]
                        target_dev = active_fz_mons.get(hw_id)
                        if not target_dev and hw_id.startswith("Display "):
                            target_dev = "\\\\.\\DISPLAY" + hw_id.replace("Display ", "")
                        elif not target_dev and hw_id == "Display Principal":
                            prim = next((m for m in monitors_info if m.get("is_primary")), None)
                            if prim: target_dev = prim["device"]

                    if target_dev:
                        for mi in monitors_info:
                            if mi["device"] == target_dev:
                                m_dev = mi["device"]
                                m_eidx = mi["enum_idx"]
                                break
                    elif mon.startswith("Pantalla ") or mon.startswith("Monitor "):
                        try: 
                            num_str = mon.replace("Monitor ", "").replace("Pantalla ", "").split(" ")[0]
                            m_idx = int(num_str) - 1
                            if 0 <= m_idx < len(monitors_info): 
                                m_dev = monitors_info[m_idx]["device"]
                                m_eidx = monitors_info[m_idx]["enum_idx"]
                        except: pass
                        
                    layout_uuid = None
                    zone_idx = 0
                    zone_name = item.get('fancyzone', 'Ninguna')
                    if zone_name != 'Ninguna':
                        parts = zone_name.rsplit(" - Zona ", 1)
                        if len(parts) == 2:
                            lname = parts[0]
                            try: zone_idx = int(parts[1].split()[0]) - 1
                            except: pass
                            
                            li = self.available_layouts.get(lname)
                            if li: layout_uuid = li.get("uuid")

                    intent = copy.deepcopy(item)
                    intent["_d_guid"] = _norm(d_guid)
                    intent["_m_dev"] = str(m_dev).lower()
                    intent["_m_eidx"] = m_eidx
                    intent["_l_uuid"] = _norm(layout_uuid)
                    intent["_z_idx"] = zone_idx
                    
                    # Generar matchers de búsqueda ponderados
                    v_paths = set()
                    s_kws = set()
                    w_kws = set()
                    
                    path = str(item.get("path", "")).lower()
                    t = item.get("type", "exe")
                    c = str(item.get('cmd', '')).lower()
                    
                    if path:
                        v_paths.add(os.path.normpath(path).lower())
                        base = os.path.basename(path).lower()
                        if t in ['vscode', 'ide']: 
                            s_kws.add(base)
                        elif t == 'obsidian':
                            s_kws.add(base)
                            w_kws.add("obsidian")
                        elif t == 'powershell':
                            s_kws.add(base)
                            w_kws.update(["terminal", "powershell", "cmd.exe"])
                        elif t == 'url':
                            try: s_kws.add(_extract_domain_kw(path))
                            except: pass
                        elif t in ['exe', 'app'] or not t:
                            s_kws.add(base.replace(".exe", ""))
                            
                    if c:
                        for chunk in c.split(TAB_SEPARATOR.lower()):
                            c_clean = chunk.strip()
                            if c_clean:
                                if c_clean.startswith("http"):
                                    try: s_kws.add(_extract_domain_kw(c_clean))
                                    except: pass
                                else:
                                    v_paths.add(c_clean)
                                    
                    if t == 'url':
                        b_cmd = item.get('browser', '').lower()
                        if b_cmd and b_cmd != 'default':
                            w_kws.add(os.path.basename(b_cmd).replace(".exe", ""))
                        else:
                            w_kws.update(["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"])
                            
                    intent["_match_paths"] = {p for p in v_paths if len(p) > 2}
                    intent["_strong_kws"] = {k for k in s_kws if len(k) > 2}
                    intent["_weak_kws"] = {k for k in w_kws if len(k) > 2}
                    
                    target_proc = ""
                    if t in ['vscode', 'ide']:
                        if t == 'vscode': target_proc = "code"
                        elif t == 'ide': 
                            ide_cmd = item.get('ide_cmd', '').lower()
                            if ide_cmd: target_proc = os.path.basename(ide_cmd).replace(".exe", "")
                    intent["_target_proc"] = target_proc
                    
                    intent["_matched_hwnds"] = []
                    
                    unmatched_intents.append(intent)

                print(f"\n[Recover] Iniciando. {len(unmatched_intents)} intents configurados.\n")
                
                self.zone_stacks.clear()
                
                def _get_process_path(hwnd):
                    try:
                        import psutil
                        _, pid = win32process.GetWindowThreadProcessId(hwnd)
                        return psutil.Process(pid).exe().lower()
                    except: return ""

                def _enum_cb(hwnd, _):
                    if not win32gui.IsWindowVisible(hwnd): return
                    if not win32gui.GetWindowText(hwnd): return
                    
                    cls_name = win32gui.GetClassName(hwnd)
                    if cls_name in ["Progman", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow"]: return
                    
                    p_path = _get_process_path(hwnd)
                    p_name = os.path.basename(p_path).lower()
                    win_title = win32gui.GetWindowText(hwnd).lower()
                    is_explorer = (cls_name == "CabinetWClass" or "explorer.exe" in p_name)
                    
                    best_intent = None
                    best_reason = ""
                    best_score = 0
                    
                    # Sistema de puntuación para evitar emparejamientos genéricos (ej: cualquier pestaña de Chrome)
                    for intent in unmatched_intents:
                        score = 0
                        reason = ""
                        
                        target_proc = intent.get("_target_proc", "")
                        # No omitimos el intent por proceso, pero lo usaremos para bajar la puntuación si no coincide
                        # excepto en casos muy raros. Solo omitimos si es un proceso totalmente irrelevante como explorer para webs/code.
                        if target_proc and "explorer" in p_name and target_proc not in ["explorer", ""]:
                            continue 
                        
                        # 1. Path match (Score 10)
                        for vp in intent["_match_paths"]:
                            if vp in p_path:
                                score = 10
                                reason = f"Ruta exacta ({vp})"
                                break
                            elif vp in win_title:   
                                if target_proc and target_proc not in p_name:
                                    # Si es explorer intentando entrar en VSCode/IDE -> Score 2 (Genérico)
                                    if is_explorer and intent.get("type") in ["vscode", "ide"]:
                                        score = 2
                                    else:
                                        score = 4
                                else:
                                    score = 9
                                reason = f"Ruta en título ({vp})"
                                break
                        
                        # 2. Strong KWs (Score 8)
                        if score == 0:
                            for skw in intent["_strong_kws"]:
                                if skw in win_title:
                                    if target_proc and target_proc not in p_name:
                                        if is_explorer and intent.get("type") in ["vscode", "ide"]:
                                            score = 2
                                        else:
                                            score = 4
                                    else:
                                        score = 8
                                    reason = f"Keyword Exacta ({skw})"
                                    break
                                
                                if intent.get("type", "") in ['exe', 'app'] and skw in p_path:
                                    score = 8
                                    reason = f"Proceso ({skw})"
                                    break
                                    
                        # 3. Weak KWs (Score 3)
                        if score == 0:
                            for wkw in intent["_weak_kws"]:
                                if wkw in p_path or wkw in p_name or wkw in win_title:
                                    score = 3
                                    reason = f"Genérico ({wkw})"
                                    break
                                    
                        if score > best_score:
                            best_score = score
                            best_intent = intent
                            best_reason = reason
                        elif score > 0 and score == best_score and best_intent is None:
                            best_intent = intent
                            best_reason = reason
                        elif score > 0 and score == best_score and best_intent:
                            if len(intent["_matched_hwnds"]) < len(best_intent["_matched_hwnds"]):
                                best_intent = intent
                                best_reason = reason
                            
                    if best_intent:
                        best_intent["_matched_hwnds"].append((hwnd, best_score))
                        print(f"  [RECUPERADA] -> '{win_title[:30]}...' -> Zona {best_intent['_z_idx']+1} ({best_reason})")
                    else:
                        print(f"  [IGNORADA] {win_title[:40]}... (No coincide con config)")
                            
                win32gui.EnumWindows(_enum_cb, None)
                
                def _show_ui():
                    RecoverySelectionDialog(self, unmatched_intents, self._execute_recovery_snap_and_stack)
                    self.btn_recover.configure(state="normal")
                    
                self.after(0, _show_ui)
            except Exception as e:
                import traceback
                print(f"Error recuperando entorno (Traceback):\n{traceback.format_exc()}")
                print(f"Error recuperando entorno (Escaneo): {e}")
                self.after(0, lambda: self.btn_recover.configure(state="normal"))
                
        threading.Thread(target=_task, daemon=True).start()

    def _execute_recovery_snap_and_stack(self, intents_data):
        import win32gui, win32con, threading
        
        def _recovery_task():
            try:
                monitors_info = []
                import win32api
                try:
                    for idx, (hMonitor, _, pyRect) in enumerate(win32api.EnumDisplayMonitors()):
                        minfo = win32api.GetMonitorInfo(hMonitor)
                        monitors_info.append({
                            "device": minfo.get("Device", f"\\\\.\\DISPLAY{idx+1}"),
                            "work_area": minfo.get("Work"),
                            "bounds": pyRect,
                            "enum_idx": idx,
                            "is_primary": minfo.get("Flags", 0) == 1
                        })
                except: pass
                
                for intent in intents_data:
                    hwnds = intent.get("_matched_hwnds", [])
                    relaunch = intent.get("_relaunch", False)

                    if relaunch:
                        self._launch_and_snap_intent(intent, monitors_info)
                        continue

                    for hwnd in hwnds:
                        # Restaurar ventana de posibles minimizados o maximizados
                        if win32gui.IsIconic(hwnd):
                            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
                        placement = win32gui.GetWindowPlacement(hwnd)
                        if placement[1] == win32con.SW_SHOWMAXIMIZED:
                            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        
                        # Mover la ventana fisicamente si tiene zona asignada
                        if intent["_l_uuid"] and intent["_l_uuid"] != "00000000-0000-0000-0000-000000000000":
                            mi = next((m for m in monitors_info if m['enum_idx'] == intent["_m_eidx"]), monitors_info[0] if monitors_info else None)
                            if not mi: continue
                            
                            layout_name = next((n for n, dt in self.available_layouts.items() if dt.get("uuid") == intent["_l_uuid"]), None)
                            layout_info = self.available_layouts.get(layout_name, {}) if layout_name else {}
                            
                            rect = self._calculate_zone_rect(layout_info, intent["_z_idx"], mi['work_area'])
                            if rect:
                                z_l, z_t, z_w, z_h = rect
                                self._apply_zone_rect_with_shadow_compensation(hwnd, z_l, z_t, z_w, z_h)
                                
                                # Anadirla a nuestra base de datos de zona activa
                                z_key = self._get_zone_key(intent["_d_guid"], intent["_m_dev"], intent["_l_uuid"], intent["_z_idx"])
                                if z_key not in self.zone_stacks:
                                    self.zone_stacks[z_key] = []
                                if hwnd not in self.zone_stacks[z_key]:
                                    self.zone_stacks[z_key].append(hwnd)
                                    
                count = sum(len(v) for v in self.zone_stacks.values())
                print(f"[Recover] Reorganización completada. Vinculadas {count} ventanas en {len(self.zone_stacks)} zonas.")
                self.after(0, lambda: self._on_recover_done(count))
            except Exception as e:
                import traceback
                print(f"Error ejecutando recuperación física (Traceback):\n{traceback.format_exc()}")
                print(f"Error: {e}")
                self.after(0, lambda: self.btn_recover.configure(state="normal"))
                
        self.btn_recover.configure(state="disabled")
        threading.Thread(target=_recovery_task, daemon=True).start()

    def _on_recover_done(self, count):
        self.btn_recover.configure(state="normal")

    def show_recover_info(self):
        from tkinter import messagebox
        messagebox.showinfo("¿Qué hace Recuperar Info?", 
                            "Si has cerrado el launcher accidentalmente o las ventanas se han desorganizado, esta función escanea las ventanas abiertas.\n\n"
                            "Las ventanas que coincidan con la categoría actual serán emparejadas con sus aplicaciones y reorganizadas físicamente en sus zonas ("
                            "FancyZones) correspondientes, restaurando los grupos.\n\n"
                            "⚠️ SOLO detectará las aplicaciones que estén registradas en la categoría que tengas seleccionada actualmente.", parent=self)


    def launch_workspace(self):
        items_to_launch = self.apps_data.get(self.current_category, [])
        if not items_to_launch: return

        self.load_fancyzones_layouts()
        self.btn_launch.configure(state="disabled", text="⏳ LANZANDO ENTORNO...")

        def _launch_task():
            import win32api, win32gui, copy, time
            
            # 0. Asegurar escritorios virtuales necesarios segun la config
            created_vds, vd_errors = self._ensure_required_virtual_desktops()
            # Pequeña espera si se crearon para que el SO asiente los GUIDs
            if created_vds > 0: time.sleep(1.2)
            
            desk_guids = []
            if WINDOWS_LIBS_AVAILABLE:
                try:
                    from pyvda import get_virtual_desktops, VirtualDesktop
                    desktops = get_virtual_desktops()
                    desk_guids = [d.id for d in desktops]
                except: pass
                
            active_fz_mons = {}
            if WINDOWS_LIBS_AVAILABLE:
                try:
                    i = 0
                    while True:
                        d = win32api.EnumDisplayDevices(None, i, 0)
                        if not d.DeviceName: break
                        if d.StateFlags & 1:
                            m_i = 0
                            while True:
                                try:
                                    m = win32api.EnumDisplayDevices(d.DeviceName, m_i, 0)
                                    if not m.DeviceID: break
                                    parts = m.DeviceID.split("\\")
                                    if len(parts) > 1 and parts[0] == "MONITOR":
                                        active_fz_mons[parts[1]] = d.DeviceName
                                    m_i += 1
                                except: break
                        i += 1
                except Exception: pass

            monitors_info = []
            try:
                for idx, (hMonitor, _, pyRect) in enumerate(win32api.EnumDisplayMonitors()):
                    minfo = win32api.GetMonitorInfo(hMonitor)
                    monitors_info.append({
                        "device": minfo.get("Device", f"\\\\.\\DISPLAY{idx+1}"),
                        "work_area": minfo.get("Work"),
                        "bounds": pyRect,
                        "enum_idx": idx,
                        "is_primary": minfo.get("Flags", 0) == 1
                    })
            except: pass

            intents = []
            for item in items_to_launch:
                desktop = item.get('desktop', 'Por defecto')
                mon = item.get('monitor', 'Por defecto')
                
                d_guid = None
                if desktop.startswith("Escritorio "):
                    try: 
                        d_idx = int(desktop.split(" ")[1]) - 1
                        if 0 <= d_idx < len(desk_guids): d_guid = desk_guids[d_idx]
                    except: pass
                
                # Si no hay escritorio explícito, usar el escritorio ACTUAL real
                # para que la clave del grupo coincida con la detección dinámica
                if d_guid is None:
                    try:
                        from pyvda import VirtualDesktop
                        d_guid = VirtualDesktop.current().id
                    except: pass
                    
                m_dev = monitors_info[0]["device"] if monitors_info else "\\\\.\\DISPLAY1"
                m_eidx = 0
                
                target_dev = None
                if "[" in mon and "]" in mon:
                    hw_id = mon.split("[")[1].split("]")[0]
                    target_dev = active_fz_mons.get(hw_id)
                    if not target_dev and hw_id.startswith("Display "):
                        target_dev = "\\\\.\\DISPLAY" + hw_id.replace("Display ", "")
                    elif not target_dev and hw_id == "Display Principal":
                        prim = next((m for m in monitors_info if m.get("is_primary")), None)
                        if prim: target_dev = prim["device"]

                if target_dev:
                    for mi in monitors_info:
                        if mi["device"] == target_dev:
                            m_dev = mi["device"]
                            m_eidx = mi["enum_idx"]
                            break
                elif mon.startswith("Pantalla ") or mon.startswith("Monitor "):
                    try: 
                        num_str = mon.replace("Monitor ", "").replace("Pantalla ", "").split(" ")[0]
                        m_idx = int(num_str) - 1
                        if 0 <= m_idx < len(monitors_info): 
                            m_dev = monitors_info[m_idx]["device"]
                            m_eidx = monitors_info[m_idx]["enum_idx"]
                    except: pass
                    
                layout_uuid = None
                zone_idx = 0
                zone_name = item.get('fancyzone', 'Ninguna')
                if zone_name != 'Ninguna':
                    parts = zone_name.rsplit(" - Zona ", 1)
                    if len(parts) == 2:
                        lname = parts[0]
                        try: zone_idx = int(parts[1].split()[0]) - 1
                        except: pass
                        
                        li = self.available_layouts.get(lname)
                        if li: layout_uuid = li.get("uuid")

                def _norm(gid):
                    if not gid: return "00000000-0000-0000-0000-000000000000"
                    return str(gid).strip("{}").lower()

                intent = copy.deepcopy(item)
                intent["_d_guid"] = _norm(d_guid)
                intent["_m_dev"] = str(m_dev).lower()
                intent["_m_eidx"] = m_eidx
                intent["_l_uuid"] = _norm(layout_uuid)
                intent["_z_idx"] = zone_idx
                intents.append(intent)

            from collections import defaultdict
            groups = defaultdict(lambda: defaultdict(list))
            for intt in intents:
                groups[intt["_d_guid"]][intt["_m_dev"]].append(intt)

            for dguid in groups:
                if dguid and dguid != "00000000-0000-0000-0000-000000000000" and WINDOWS_LIBS_AVAILABLE:
                    try:
                        from pyvda import get_virtual_desktops, VirtualDesktop
                        target_d = next((d for d in get_virtual_desktops() if str(d.id).strip("{}").lower() == dguid), None)
                        if target_d:
                            target_d.go()
                            if not self._wait_for_condition(lambda: str(VirtualDesktop.current().id).strip("{}").lower() == dguid, timeout=4.0):
                                print(f"[ERROR DURO] Cambio fallido al escritorio: {dguid}")
                                continue
                    except Exception as e:
                        print(f"Desktop Switch Error: {e}")

                for mdev in groups[dguid]:
                    cur_items = groups[dguid][mdev]
                    uuid = cur_items[0]["_l_uuid"]
                    meidx = cur_items[0]["_m_eidx"]
                    if uuid:
                        self.apply_fz_layout_cli(uuid, meidx + 1)
                        time.sleep(0.3)

                    # Lanzar ventanas secuencialmente (el paralelo confunde detección de hwnds)
                    for intt in cur_items:
                        self._launch_and_snap_intent(intt, monitors_info)

            # === REPASO FINAL: Re-posicionar todas las ventanas que se hayan movido ===
            time.sleep(1.5)  # Esperar a que todo se asiente
            print("[Repaso] Verificando posiciones finales...")
            for z_key, hwnds in list(self.zone_stacks.items()):
                if len(z_key) < 4: continue
                # Buscar el layout y la zona para recalcular la posición correcta
                l_uuid_key = z_key[2]
                z_idx_key = z_key[3]
                
                layout_name = next((n for n, dt in self.available_layouts.items() if dt.get("uuid") == l_uuid_key), None)
                if not layout_name: continue
                layout_info = self.available_layouts[layout_name]
                
                # Buscar el monitor correcto
                m_dev_key = z_key[1]
                mi = next((m for m in monitors_info if m['device'].lower() == m_dev_key), monitors_info[0] if monitors_info else None)
                if not mi: continue
                
                rect = self._calculate_zone_rect(layout_info, z_idx_key, mi['work_area'])
                if not rect: continue
                z_l, z_t, z_w, z_h = rect
                
                for hwnd in hwnds:
                    if not win32gui.IsWindow(hwnd): continue
                    try:
                        cur_rect = win32gui.GetWindowRect(hwnd)
                        cl, ct, cr, cb = cur_rect
                        # Si la ventana se ha movido significativamente de su posición correcta
                        if abs(cl - z_l) > 50 or abs(ct - z_t) > 50 or abs((cr-cl) - z_w) > 50 or abs((cb-ct) - z_h) > 50:
                            self._apply_zone_rect_with_shadow_compensation(hwnd, z_l, z_t, z_w, z_h)
                            print(f"[Repaso] Reposicionada hwnd={hwnd} en zona {z_idx_key}")
                    except: pass
            print("[Repaso] Posiciones verificadas ✓")
            self.after(0, lambda: self.btn_launch.configure(state="normal", text="🚀 LANZAR ENTORNO"))

        import threading
        threading.Thread(target=_launch_task, daemon=True).start()

    def _get_zone_key(self, d_guid, m_dev, l_uuid, z_idx):
        """Genera una clave de zona normalizada (lower, sin llaves) para consistencia en zone_stacks."""
        def _norm(gid):
            if not gid: return "00000000-0000-0000-0000-000000000000"
            return str(gid).strip("{}").lower()
        return (_norm(d_guid), str(m_dev).lower(), _norm(l_uuid), int(z_idx))

    def _detect_zone_for_window(self, hwnd):
        """Devuelve la clave de zona (d_guid, m_dev, l_uuid, z_idx) en la que se encuentra la ventana físicamente, o None."""
        if not hwnd or not WINDOWS_LIBS_AVAILABLE: return None
        import win32gui, win32api, json, os

        if not win32gui.IsWindow(hwnd): return None
        
        # 1. Leer layouts aplicados de PowerToys (Origen de la verdad actual)
        pt_applied = os.path.join(self.fancyzones_path, "applied-layouts.json")
        if not os.path.exists(pt_applied): return None
        
        try:
            with open(pt_applied, 'r', encoding='utf-8') as f:
                pt_data = json.load(f)
        except: return None
        
        applied_list = pt_data.get("applied-layouts", [])
        if not applied_list: return None
        
        # 2. Info entorno actual
        d_guid = "00000000-0000-0000-0000-000000000000"
        try:
            from pyvda import VirtualDesktop
            d_guid = str(VirtualDesktop.current().id)
        except: pass
        
        rect = win32gui.GetWindowRect(hwnd)
        wl, wt, wr, wb = rect
        wcx, wcy = wl + (wr - wl)//2, wt + (wb - wt)//2
        ww, wh = wr - wl, wb - wt
        
        active_fz_mons = {}
        if WINDOWS_LIBS_AVAILABLE:
            try:
                i = 0
                while True:
                    d = win32api.EnumDisplayDevices(None, i, 0)
                    if not d.DeviceName: break
                    if d.StateFlags & 1:
                        m_i = 0
                        while True:
                            try:
                                m = win32api.EnumDisplayDevices(d.DeviceName, m_i, 0)
                                if not m.DeviceID: break
                                parts = m.DeviceID.split("\\")
                                if len(parts) > 1 and parts[0] == "MONITOR":
                                    active_fz_mons[parts[1]] = d.DeviceName
                                m_i += 1
                            except: break
                    i += 1
            except Exception: pass
            
        monitors_info = []
        try:
            for idx, (hMonitor, _, pyRect) in enumerate(win32api.EnumDisplayMonitors()):
                 minfo = win32api.GetMonitorInfo(hMonitor)
                 monitors_info.append({
                     "device": minfo.get("Device", ""),
                     "work_area": minfo.get("Work"),
                     "idx": idx
                 })
        except: pass

        def _norm_id(gid):
            if not gid: return "00000000-0000-0000-0000-000000000000"
            return str(gid).strip("{}").lower()

        # 3. Emparejar con zonas comparando el centro de la ventana
        for entry in applied_list:
            # Normalizar el virtual-desktop del entry para comparar
            dev = entry.get("device", {})
            e_d_guid = _norm_id(dev.get("virtual-desktop", ""))
            
            if e_d_guid != "00000000-0000-0000-0000-000000000000" and e_d_guid != _norm_id(d_guid):
                continue
            
            l_info = entry.get("applied-layout", {})
            l_uuid = _norm_id(l_info.get("uuid", ""))  # Normalizado para buscar en available_layouts
            if not l_uuid or l_uuid == "00000000-0000-0000-0000-000000000000": continue
            
            # Buscar el layout comparando UUIDs normalizados
            lname = next((n for n, d in self.available_layouts.items() if _norm_id(d.get("uuid", "")) == l_uuid), None)
            if not lname:
                continue
            layout_data = self.available_layouts[lname]
            
            num_zones = 0
            if layout_data.get("type") == "grid":
                for row in layout_data.get("cell-child-map", []):
                    for cell in row: num_zones = max(num_zones, cell + 1)
            else:
                num_zones = len(layout_data.get("zones", []))

            entry_target_dev_id = dev.get("monitor", "")
            target_device = active_fz_mons.get(entry_target_dev_id)
            if not target_device and entry_target_dev_id.startswith("Display "):
                target_device = "\\\\.\\DISPLAY" + entry_target_dev_id.replace("Display ", "")
                
            for mi in monitors_info:
                if target_device and mi['device'].lower() != target_device.lower():
                    continue
                for z_idx in range(num_zones):
                    z_rect = self._calculate_zone_rect(layout_data, z_idx, mi['work_area'])
                    if z_rect:
                        zl, zt, zw, zh = z_rect
                        # Usar SOLO el centro para determinar la zona - sin restricción de tamaño.
                        # FancyZones cambia el tamaño de la ventana, pero el centro siempre
                        # cae dentro de la zona correcta.
                        if zl <= wcx <= zl + zw and zt <= wcy <= zt + zh:
                            key = self._get_zone_key(_norm_id(d_guid), mi['device'], l_uuid, z_idx)
                            print(f"[Detect OK] hwnd={hwnd} -> zone={z_idx} layout={lname} key={key}")
                            return key
        print(f"[Detect FAIL] hwnd={hwnd} cx={wcx} cy={wcy} w={ww} h={wh}")
        return None

    def _get_process_path(self, hwnd):
        try:
            import win32process, ctypes, os
            from ctypes import wintypes
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
            hProcess = kernel32.OpenProcess(0x1000, False, pid)
            if not hProcess: return ""
            exe_path = ctypes.create_unicode_buffer(260)
            size = wintypes.DWORD(260)
            success = kernel32.QueryFullProcessImageNameW(hProcess, 0, exe_path, ctypes.byref(size))
            kernel32.CloseHandle(hProcess)
            if success: return os.path.normpath(exe_path.value).lower()
        except: pass
        return ""

    def _calculate_zone_rect(self, layout_info, zone_idx, work_area):
        ltype = layout_info.get("type", "grid")
        left_bound, top_bound, right_bound, bottom_bound = work_area
        width = right_bound - left_bound
        height = bottom_bound - top_bound
        spacing = layout_info.get("spacing", 0) if layout_info.get("show-spacing", True) else 0
        
        if ltype == "grid":
            rows_perc = layout_info.get("rows-percentage", [10000])
            cols_perc = layout_info.get("columns-percentage", [10000])
            cell_map = layout_info.get("cell-child-map", [[0]])
            total_r = sum(rows_perc) if sum(rows_perc) > 0 else 10000
            total_c = sum(cols_perc) if sum(cols_perc) > 0 else 10000
            row_bounds = [top_bound]
            accum = 0
            for p in rows_perc:
                accum += p
                row_bounds.append(top_bound + int((accum / total_r) * height))
            col_bounds = [left_bound]
            accum = 0
            for p in cols_perc:
                accum += p
                col_bounds.append(left_bound + int((accum / total_c) * width))
            min_r, max_r = 9999, -1
            min_c, max_c = 9999, -1
            for r_i, row in enumerate(cell_map):
                for c_i, z_val in enumerate(row):
                    if z_val == zone_idx:
                        min_r, max_r = min(min_r, r_i), max(max_r, r_i)
                        min_c, max_c = min(min_c, c_i), max(max_c, c_i)
            if min_r <= max_r and min_c <= max_c:
                z_t = row_bounds[min_r] + spacing
                z_b = row_bounds[max_r + 1] - spacing
                z_l = col_bounds[min_c] + spacing
                z_r = col_bounds[max_c + 1] - spacing
                return (z_l, z_t, max(50, z_r - z_l), max(50, z_b - z_t))
        elif ltype == "canvas":
            ref_w = layout_info.get("ref-width", width)
            if ref_w <= 0: ref_w = width
            ref_h = layout_info.get("ref-height", height)
            if ref_h <= 0: ref_h = height
            zones = layout_info.get("zones", [])
            if zone_idx < len(zones):
                z = zones[zone_idx]
                x = left_bound + int((z.get("X", 0) / ref_w) * width)
                y = top_bound + int((z.get("Y", 0) / ref_h) * height)
                w = int((z.get("width", 100) / ref_w) * width)
                h = int((z.get("height", 100) / ref_h) * height)
                return (x, y, w, h)
        return None

    def _apply_zone_rect_with_shadow_compensation(self, hwnd, z_l, z_t, z_w, z_h):
        import win32gui, win32con, ctypes
        from ctypes.wintypes import RECT

        try:
            rect = win32gui.GetWindowRect(hwnd)
            logical_l, logical_t, logical_r, logical_b = rect

            DWMWA_EXTENDED_FRAME_BOUNDS = 9
            visual_rect = RECT()
            if ctypes.windll.dwmapi.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ctypes.byref(visual_rect), ctypes.sizeof(visual_rect)) == 0:
                visual_l, visual_t = visual_rect.left, visual_rect.top
                visual_r, visual_b = visual_rect.right, visual_rect.bottom
                
                offset_l = visual_l - logical_l
                offset_t = visual_t - logical_t
                offset_r = logical_r - visual_r
                offset_b = logical_b - visual_b

                new_l = z_l - offset_l
                new_t = z_t - offset_t
                new_w = z_w + offset_l + offset_r
                new_h = z_h + offset_t + offset_b

                win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, new_l, new_t, new_w, new_h, win32con.SWP_SHOWWINDOW)
                return True
        except Exception as e:
            pass

        win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, z_l, z_t, z_w, z_h, win32con.SWP_SHOWWINDOW)
        return False

    def _launch_and_snap_intent(self, intent, monitors_info):
        import win32gui, win32con, os, subprocess, time, shutil, webbrowser, urllib.parse
        t = intent.get('type')
        p = intent.get('path')
        delay_s = float(intent.get('delay', '0') or 0)
        
        if delay_s > 0: time.sleep(delay_s)

        hwnds_before = set()
        def enum_before(hwnd, _):
            if win32gui.IsWindowVisible(hwnd): hwnds_before.add(hwnd)
        win32gui.EnumWindows(enum_before, 0)
        
        process = None
        try:
            if t == 'url':
                browser_cmd = intent.get('browser', 'default')
                cmds_raw = intent.get('cmd', '')
                urls_to_open = []
                if cmds_raw:
                    for u in cmds_raw.split(TAB_SEPARATOR):
                        cu = u.strip()
                        if cu: urls_to_open.append(cu)
                else:
                    urls_to_open = [p]
                
                if not urls_to_open:
                    urls_to_open = [p]
                
                # Siempre abrir en ventana NUEVA y aparte
                # Primera URL: --new-window, el resto se pasan como argumentos al mismo comando
                all_urls_quoted = ' '.join(f'"{u}"' for u in urls_to_open)
                
                if browser_cmd == 'default':
                    # webbrowser.open no soporta --new-window, usamos el navegador registrado
                    import webbrowser as wb
                    try:
                        # Intentar obtener el navegador por defecto del registro
                        import winreg
                        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice") as key:
                            prog_id, _ = winreg.QueryValueEx(key, "ProgId")
                        with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, f"{prog_id}\\shell\\open\\command") as key:
                            default_cmd, _ = winreg.QueryValueEx(key, "")
                        # Extraer el ejecutable
                        default_exe = default_cmd.split('"')[1] if '"' in default_cmd else default_cmd.split(' ')[0]
                        subprocess.Popen(f'"{default_exe}" --new-window {all_urls_quoted}', shell=True)
                    except:
                        # Fallback: abrir primera URL con webbrowser, resto como tabs
                        wb.open_new(urls_to_open[0])
                        for extra_url in urls_to_open[1:]:
                            time.sleep(0.3)
                            wb.open_new_tab(extra_url)
                elif os.path.exists(browser_cmd):
                    subprocess.Popen(f'"{browser_cmd}" --new-window {all_urls_quoted}', shell=True)
                else:
                    subprocess.Popen(f'start {browser_cmd} --new-window {all_urls_quoted}', shell=True)
            elif t == 'vscode': process = subprocess.Popen(f'code "{p}"', shell=True)
            elif t == 'ide': process = subprocess.Popen(f'{intent.get("ide_cmd", "code")} "{p}"', shell=True)
            elif t == 'exe': 
                try: process = subprocess.Popen(p)
                except OSError: os.startfile(p)
            elif t == 'obsidian':
                encoded = urllib.parse.quote(p)
                webbrowser.open(f"obsidian://open?path={encoded}")
            elif t == 'powershell':
                tabs_content = intent.get('cmd', '').split(TAB_SEPARATOR)
                def f_ps(ct, bp):
                    ct = ct.strip()
                    if not ct: return ""
                    tp = ct.strip("'\"")
                    ap = os.path.join(bp, tp) if not os.path.isabs(tp) else tp
                    if os.path.isdir(ap): return f"Set-Location '{tp}'"
                    if os.path.isfile(ap): return f"& '{tp}'"
                    pts = ct.split(" ")
                    for i in range(len(pts), 0, -1):
                        pp = " ".join(pts[:i]).strip("'\"")
                        if not pp: continue
                        app = os.path.join(bp, pp) if not os.path.isabs(pp) else pp
                        if os.path.isfile(app): return f"& '{pp}' {' '.join(pts[i:])}".strip()
                    return ct
                    
                if shutil.which("wt") is None:
                    sc = f_ps(tabs_content[0], p).replace(";", " & ")
                    process = subprocess.Popen(f'start powershell -NoExit -Command "Set-Location \'{p}\'; {sc}"', shell=True)
                else:
                    wta = ["wt", "-w", "-1", "-d", p, "powershell", "-NoExit"]
                    if f_ps(tabs_content[0], p): wta.extend(["-Command", f_ps(tabs_content[0], p)])
                    for ct in tabs_content[1:]:
                        wta.extend([";", "new-tab", "-d", p, "powershell", "-NoExit"])
                        if f_ps(ct, p): wta.extend(["-Command", f_ps(ct, p)])
                    process = subprocess.Popen(wta)
        except Exception as e:
            print(f"[ERROR DURO] Fallo al iniciar {t}: {e}")
            return

        if not WINDOWS_LIBS_AVAILABLE or not intent["_l_uuid"] or intent.get('fancyzone', 'Ninguna') == 'Ninguna':
            return

        matched_hwnd = None
        if process and process.pid:
            matched_hwnd = self._wait_for_condition(lambda: (self._get_hwnds_for_pid(process.pid) or [None])[0], timeout=6.0)
            
        if not matched_hwnd:
            def check_new():
                nh = []
                def ea(h, _):
                    if win32gui.IsWindowVisible(h) and h not in hwnds_before:
                        tit = win32gui.GetWindowText(h)
                        if tit and tit != "Program Manager": nh.append(h)
                win32gui.EnumWindows(ea, 0)
                target_kws = ['edge', 'chrome', 'firefox', 'brave'] if t == 'url' else \
                             ['code', 'visual studio'] if t == 'vscode' else \
                             [str(intent.get('ide_cmd', '')).lower().replace(".exe", "")] if t == 'ide' else \
                             ['obsidian'] if t == 'obsidian' else \
                             ['terminal', 'powershell'] if t == 'powershell' else \
                             [os.path.basename(p).lower().replace(".exe", "")]
                for h in nh:
                    if any(kw in win32gui.GetWindowText(h).lower() for kw in target_kws) or \
                       any(kw in self._get_process_path(h) for kw in target_kws): return h
                return nh[0] if nh else None
            matched_hwnd = self._wait_for_condition(check_new, timeout=6.0)

        if not matched_hwnd:
            print(f"[ERROR DURO] Timeout esperando ventana para: {intent['path']}")
            return

        # Prepare Window
        if win32gui.IsIconic(matched_hwnd):
            win32gui.ShowWindow(matched_hwnd, win32con.SW_RESTORE)
        placement = win32gui.GetWindowPlacement(matched_hwnd)
        if placement[1] == win32con.SW_SHOWMAXIMIZED:
            win32gui.ShowWindow(matched_hwnd, win32con.SW_RESTORE)

        if not self._force_foreground(matched_hwnd):
            print(f"[ERROR DURO] Foco denegado para: {intent['path']}")
            # Seguimos intentando moverlo, el snap absoluto puede funcionar sin foco completo.
            
        mi = next((m for m in monitors_info if m['enum_idx'] == intent["_m_eidx"]), monitors_info[0])
        l, t_y, r, b = mi['work_area']
        
        layout_name = next((n for n, dt in self.available_layouts.items() if dt.get("uuid") == intent["_l_uuid"]), None)
        layout_info = self.available_layouts.get(layout_name, {}) if layout_name else {}
        rect = self._calculate_zone_rect(layout_info, intent["_z_idx"], mi['work_area'])
        
        if rect:
            z_l, z_t, z_w, z_h = rect
            # Snapshot Matemático Directo: con compensación de bordes invisibles DWM
            self._apply_zone_rect_with_shadow_compensation(matched_hwnd, z_l, z_t, z_w, z_h)
            
            # Custom Runtime Engine: Registrar ventana internamente en su zona en lugar de usar FancyZones
            z_key = self._get_zone_key(intent["_d_guid"], intent["_m_dev"], intent["_l_uuid"], intent["_z_idx"])
            if z_key not in self.zone_stacks:
                self.zone_stacks[z_key] = []
            
            # Guardamos la HWND al final de la pila (Stack de Render/Rotación)
            if matched_hwnd not in self.zone_stacks[z_key]:
                self.zone_stacks[z_key].append(matched_hwnd)
            print(f"[Launch] hwnd={matched_hwnd} -> key={z_key} stack={self.zone_stacks[z_key]}")
                
        else:
            # Fallback a centrado si no hay layout (o error FZ)
            cen_x, cen_y = l + (r-l)//2, t_y + (b-t_y)//2
            win32gui.SetWindowPos(matched_hwnd, win32con.HWND_TOP, cen_x - 400, cen_y - 300, 800, 600, win32con.SWP_SHOWWINDOW)

    # ─────────────────────────────────────────────────────────────────────────
    #  PiP WATCHER – Anclar ventanas flotantes de vídeo a todos los escritorios
    # ─────────────────────────────────────────────────────────────────────────
    PIP_WINDOW_TITLES = (
        "imagen con imagen incrustada",   # Edge (español)
        "imagen en imagen",               # Chrome (español)
        "picture in picture",             # Chrome/Edge (inglés)
        "picture-in-picture",             # Variante con guión
    )

    def _start_pip_watcher(self):
        """Inicia el hilo en segundo plano que monitoriza ventanas PiP."""
        if self._pip_watcher_active:
            return  # Ya está corriendo
        self._pip_watcher_active = True
        self._pip_pinned_hwnds = set()
        self._pip_watcher_thread = threading.Thread(
            target=self._pip_watcher_loop, daemon=True
        )
        self._pip_watcher_thread.start()
        print("[PiP Watcher] Iniciado")

    def _stop_pip_watcher(self):
        """Detiene el hilo del watcher PiP."""
        self._pip_watcher_active = False
        self._pip_pinned_hwnds.clear()
        print("[PiP Watcher] Detenido")

    def _pip_watcher_loop(self):
        """Loop que comprueba cada segundo si hay nuevas ventanas PiP."""
        while self._pip_watcher_active:
            try:
                self._pip_scan_and_pin()
            except Exception as e:
                print(f"[PiP Watcher] Error en escaneo: {e}")
            time.sleep(1)

    def _pip_scan_and_pin(self):
        """Escanea las ventanas visibles buscando títulos PiP y las ancla."""
        if not WINDOWS_LIBS_AVAILABLE:
            return

        # 1. Limpiar el registro de ventanas ancladas:
        # Si una ventana ya no existe, no es visible o ha cambiado su título,
        # la sacamos de 'pinned_hwnds' para que pueda volver a ser detectada.
        stale = set()
        for h in self._pip_pinned_hwnds:
            try:
                if not win32gui.IsWindow(h) or not win32gui.IsWindowVisible(h):
                    stale.add(h)
                else:
                    title = win32gui.GetWindowText(h).lower()
                    if not any(pt in title for pt in self.PIP_WINDOW_TITLES):
                        stale.add(h)
            except Exception:
                stale.add(h)
        
        if stale:
            self._pip_pinned_hwnds -= stale

        pip_hwnds = []

        def _enum_pip(hwnd, _):
            """Callback de EnumWindows: recopila HWNDs de ventanas PiP."""
            # Si ya la tenemos anclada y registrada, saltar
            if hwnd in self._pip_pinned_hwnds:
                return True
            try:
                if not win32gui.IsWindowVisible(hwnd):
                    return True
                title = win32gui.GetWindowText(hwnd)
                if not title:
                    return True
                title_lower = title.strip().lower()
                for pip_title in self.PIP_WINDOW_TITLES:
                    if pip_title in title_lower:
                        pip_hwnds.append(hwnd)
                        return True
            except Exception:
                pass
            return True

        win32gui.EnumWindows(_enum_pip, None)

        for hwnd in pip_hwnds:
            try:
                if not win32gui.IsWindow(hwnd):
                    continue
                from pyvda import AppView
                view = AppView(hwnd)
                view.pin()
                self._pip_pinned_hwnds.add(hwnd)
                title = win32gui.GetWindowText(hwnd)
                print(f"[PiP Watcher] 📌 Ventana anclada: '{title}' (HWND={hwnd})")
            except Exception as e:
                print(f"[PiP Watcher] Error anclando HWND={hwnd}: {e}")


if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()