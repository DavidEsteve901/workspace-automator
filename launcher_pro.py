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

try:
    import win32api
    import win32gui
    import win32con
    import pygetwindow as gw
    from pyvda import AppView, get_virtual_desktops, VirtualDesktop
    import ctypes
    from ctypes import wintypes
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
            "util_reload_layouts": "Sistema: Recargar Layouts (Ctrl+Alt+L)"
        }
        
        ctk.CTkLabel(self, text="Configuración de Atajos Personalizados", font=("Roboto", 18, "bold")).pack(pady=(15, 5))
        ctk.CTkLabel(self, text="Instrucciones: Para editar un atajo pulsa en su botón 'Cambiar' y luego realiza \nla combinación deseada en tu teclado y/o ratón.", text_color="#aaa").pack(pady=(0, 15))
        
        self.scroll = ctk.CTkScrollableFrame(self, fg_color="#2B2B2B")
        self.scroll.pack(fill="both", expand=True, padx=20, pady=5)
        
        self.vars = {}
        for key, desc in self.desc_map.items():
            row = ctk.CTkFrame(self.scroll, fg_color="#333", corner_radius=5)
            row.pack(fill="x", pady=4, padx=5)
            
            # Nombre de la acción
            ctk.CTkLabel(row, text=desc, width=280, anchor="w", font=("Roboto", 12)).pack(side="left", padx=10, pady=8)
            
            # Valor actual
            curr_val = self.hotkeys.get(key, "Ninguno")
            v = ctk.StringVar(value=curr_val)
            self.vars[key] = v
            
            lbl_val = ctk.CTkLabel(row, textvariable=v, width=150, fg_color="#444", corner_radius=4, text_color="#2CC985", font=("Roboto", 12, "bold"))
            lbl_val.pack(side="left", padx=10)
            
            # Botón Cambiar
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
        self.parent_app._save_data()
        
        messagebox.showinfo("Guardado", "Atajos guardados. Por favor, reinicia el Launcher para que apliquen los nuevos Pynput Listeners.")
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
        if "mouse" in action_key:
            ctk.CTkLabel(self, text="Por favor, mantén pulsadas las teclas (Ej: Win o Ctrl) \ny haz el CLIC ESPERADO en esta ventana.", text_color="#aaa").pack(pady=10)
        else:
            ctk.CTkLabel(self, text="Por favor, pulsa la combinación de teclado deseada.", text_color="#aaa").pack(pady=10)
            
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
            
            # --- Modo Teclado Puro ---
            if "mouse" not in self.action_key:
                recorded_keys = set()
                
                def on_press(key):
                    if self._stop_listening: return False
                    kname = getattr(key, 'name', None)
                    if kname:
                        if kname in ['ctrl_l', 'ctrl_r']: kname = 'ctrl'
                        elif kname in ['alt_l', 'alt_r', 'alt_gr']: kname = 'alt'
                        elif kname in ['shift', 'shift_r']: kname = 'shift'
                        elif kname in ['cmd', 'cmd_r', 'cmd_l', 'win']: kname = 'win'
                    else:
                        kname = getattr(key, 'char', str(key))
                        if kname and kname.startswith('<') and kname.endswith('>'):
                            kname = kname[1:-1]
                            
                    if kname: recorded_keys.add(kname)
                    
                    # Generar string
                    mods = []
                    # Ordenar por convención
                    for m in ['ctrl', 'alt', 'shift', 'win']:
                        if m in recorded_keys: mods.append(m)
                    others = [k for k in recorded_keys if k not in ['ctrl', 'alt', 'shift', 'win']]
                    
                    self.current_combo = "+".join(mods + others)
                    self.after(0, lambda: self.lbl_result.configure(text=self.current_combo.upper()))
                    self.after(0, lambda: self.btn_save.configure(state="normal", fg_color="#2CC985"))

                def on_release(key):
                    if self._stop_listening: return False
                    
                with keyboard.Listener(on_press=on_press, on_release=on_release) as l:
                    while not self._stop_listening: time.sleep(0.1)
                
            # --- Modo Híbrido (Teclado + Ratón) ---
            else:
                kb_state = {"ctrl": False, "alt": False, "shift": False, "win": False}
                
                def on_press(key):
                    if self._stop_listening: return False
                    km = getattr(key, 'name', None)
                    if km in ['ctrl_l', 'ctrl_r']: kb_state['ctrl'] = True
                    elif km in ['alt_l', 'alt_r']: kb_state['alt'] = True
                    elif km in ['shift', 'shift_r']: kb_state['shift'] = True
                    elif km in ['cmd', 'cmd_r']: kb_state['win'] = True
                    
                def on_release(key):
                    if self._stop_listening: return False
                    km = getattr(key, 'name', None)
                    if km in ['ctrl_l', 'ctrl_r']: kb_state['ctrl'] = False
                    elif km in ['alt_l', 'alt_r']: kb_state['alt'] = False
                    elif km in ['shift', 'shift_r']: kb_state['shift'] = False
                    elif km in ['cmd', 'cmd_r']: kb_state['win'] = False
                    
                def on_click(x, y, button, pressed):
                    if self._stop_listening: return False
                    if pressed:
                        btn_name = button.name
                        mods = [k for k, v in kb_state.items() if v]
                        self.current_combo = "+".join(mods + [btn_name])
                        self.after(0, lambda: self.lbl_result.configure(text=self.current_combo.upper()))
                        self.after(0, lambda: self.btn_save.configure(state="normal", fg_color="#2CC985"))
                        # Una vez recibido el clic paramos de grabar
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

# ─────────────────────────────────────────────────────────────────────────────
#  APP PRINCIPAL
# ─────────────────────────────────────────────────────────────────────────────
class DevLauncherApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("PRO Workspace Launcher")
        self.geometry("1150x800")
        self.minsize(900, 600)
        
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

        # Estado global de modificadores para Pynput
        self.kb_modifiers = {"ctrl": False, "alt": False, "shift": False, "win": False}
        self.mouse_btn_states = {"left": False, "right": False, "middle": False}

        # --- MOTOR CUSTOM DE ZONAS (Sustituto de FZ Runtime) ---
        self.zone_stacks = {} # {(d_guid, m_dev, l_uuid, z_idx): [hwnd1, hwnd2]}
        self.hotkeys_active = False
        self._start_global_hotkeys()
        # ----------------------------------------------------

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
                    
                    # Cargar Hotkeys (con valores por defecto si no existen)
                    self.hotkeys_data = data.get("hotkeys", {
                        "cycle_forward": "ctrl+alt+pagedown",
                        "cycle_backward": "ctrl+alt+pageup",
                        "mouse_cycle_fwd": "win+alt+right",
                        "mouse_cycle_bwd": "win+alt+left",
                        "util_reload_layouts": "ctrl+alt+l"
                    })
        except Exception as e:
            print(f"Error cargando base de datos: {e}")
            self.hotkeys_data = {
                "cycle_forward": "ctrl+alt+pagedown", "cycle_backward": "ctrl+alt+pageup",
                "mouse_cycle_fwd": "win+alt+right", "mouse_cycle_bwd": "win+alt+left",
                "util_reload_layouts": "ctrl+alt+l"
            }

    def _save_data(self):
        try:
            data = {
                "apps": self.apps_data,
                "last_category": self.current_category,
                "applied_mappings": self.applied_mappings,
                "hotkeys": getattr(self, "hotkeys_data", {})
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
            # Mostrar diálogo avanzado después
            adv_dlg = AdvancedItemDialog(self, title=f"Configurar Proyecto ({ide_cmd})", path_or_url=path, item_type="ide")
            self.wait_window(adv_dlg)
            extras = adv_dlg.result if adv_dlg.result else {}
            
            item_data = {
                "type": "ide", 
                "path": path, 
                "ide_cmd": ide_cmd
            }
            item_data.update(extras)
            
            self.apps_data[self.current_category].append(item_data)
            self._save_data()
            self.refresh_apps_list()
            
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
                        p = normalize_kb_str(p)
                        if p in ['ctrl', 'alt', 'shift', 'win', 'cmd', 'page_down', 'page_up', 'home', 'end']:
                            res.append(f"<{p}>")
                        else:
                            res.append(p)
                    return "+".join(res)
                    
                def is_mouse_combo(kstr):
                    parts = kstr.lower().split('+')
                    # Incluimos los nombres que pynput suele dar a los botones de ratón
                    mouse_btns = ['left', 'right', 'middle', 'x1', 'x2']
                    return any(b in parts for b in mouse_btns)

                kb_mapping = {}
                hk = self.hotkeys_data
                
                # Acciones a considerar
                actions = {
                    "mouse_cycle_fwd": self._cycle_zone_forward,
                    "mouse_cycle_bwd": self._cycle_zone_backward,
                    "cycle_forward": self._cycle_zone_forward,
                    "cycle_backward": self._cycle_zone_backward,
                    "util_reload_layouts": self.load_fancyzones_layouts
                }

                # Registrar solo teclado puro en GlobalHotKeys
                for key_id, func in actions.items():
                    combo = hk.get(key_id)
                    if combo and not is_mouse_combo(combo):
                        kb_mapping[format_pynput_str(combo)] = func

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
                    if not macro_str: return False
                    parts = set(macro_str.lower().split('+'))
                    # Verificar si el botón está en la macro
                    if btn_str.lower() not in parts: return False
                    # Verificar modificadores
                    needs_ctrl = "ctrl" in parts
                    needs_alt = "alt" in parts
                    needs_shift = "shift" in parts
                    needs_win = "win" in parts or "cmd" in parts
                    return (kb_state['ctrl'] == needs_ctrl and kb_state['alt'] == needs_alt and
                            kb_state['shift'] == needs_shift and kb_state['win'] == needs_win)

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
                        hiword = (getattr(data, 'mouseData', 0) >> 16) & 0xFFFF
                        if hiword == 1: btn_name = 'x1'
                        elif hiword == 2: btn_name = 'x2'

                    if btn_name:
                        for key_id, func in actions.items():
                            combo = hk.get(key_id)
                            if combo and is_mouse_combo(combo):
                                if match_mouse_hotkey(combo, btn_name):
                                    if is_down:
                                        import threading
                                        threading.Thread(target=func, daemon=True).start()
                                    return False
                    return True

                def on_click(x, y, button, pressed):
                    if pressed:
                        btn_name = button.name # 'left', 'right', 'x1', etc.
                        for key_id, func in actions.items():
                            combo = hk.get(key_id)
                            if combo and is_mouse_combo(combo):
                                if match_mouse_hotkey(combo, btn_name):
                                    func()
                    else: # RELEASE
                        # Cuando soltamos el clic izquierdo con Shift, dejamos que FancyZones haga su 
                        # encaje visual real. Luego, pasado un momento, actualizamos su grupo.
                        if getattr(button, 'name', '') == 'left' and kb_state.get('shift'):
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
                                        detected_device = target_key[1]  # monitor device
                                        detected_z_idx = target_key[3]   # zone index
                                        
                                        # Buscar grupo existente en el mismo monitor y misma zona
                                        existing_key = None
                                        for k in self.zone_stacks:
                                            if len(k) >= 4 and k[1] == detected_device and k[3] == detected_z_idx:
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
                    now = time.time()
                    if hasattr(self, '_last_cycle_time') and (now - self._last_cycle_time) < 0.5:
                        if hasattr(self, '_last_cycle_hwnd') and self._last_cycle_hwnd in stack:
                            fg = self._last_cycle_hwnd

                    if stack and len(stack) > 1:
                        if fg in stack:
                            idx = stack.index(fg)
                            next_idx = (idx + 1) % len(stack)
                        else:
                            next_idx = 0
                        target_hwnd = stack[next_idx]
                        self._force_foreground(target_hwnd)
                        self._last_cycle_hwnd = target_hwnd
                        self._last_cycle_time = time.time()
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
                    now = time.time()
                    if hasattr(self, '_last_cycle_time') and (now - self._last_cycle_time) < 0.5:
                        if hasattr(self, '_last_cycle_hwnd') and self._last_cycle_hwnd in stack:
                            fg = self._last_cycle_hwnd

                    if stack and len(stack) > 1:
                        if fg in stack:
                            idx = stack.index(fg)
                            next_idx = (idx - 1) % len(stack)
                        else:
                            next_idx = len(stack) - 1
                        target_hwnd = stack[next_idx]
                        self._force_foreground(target_hwnd)
                        self._last_cycle_hwnd = target_hwnd
                        self._last_cycle_time = time.time()
                except Exception as e:
                    print(f"Error en ciclo backward: {e}")
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
        import win32gui, win32process, threading, os
        
        # Load layouts directly before detecting anything
        self.load_fancyzones_layouts()
        self.btn_recover.configure(state="disabled")
        
        items_to_launch = self.apps_data.get(self.current_category, [])
        valid_paths = set()
        valid_kws = set()
        for item in items_to_launch:
            path = str(item.get("path", "")).lower()
            t = item.get("type", "exe")
            c = str(item.get('cmd', '')).lower()
            
            def _extract_domain_kw(url_str):
                d = url_str.replace("https://", "").replace("http://", "").split("/")[0]
                d = d.replace("www.", "")
                return d.split(".")[0] if "." in d else d

            if path:
                valid_paths.add(os.path.normpath(path).lower())
                base = os.path.basename(path).lower()
                if t in ['vscode', 'ide']:
                    valid_kws.add(base)
                elif t == 'obsidian':
                    valid_kws.add(base)
                    valid_kws.add("obsidian")
                elif t == 'powershell':
                    valid_kws.add(base)
                    valid_kws.add("terminal")
                    valid_kws.add("powershell")
                elif t == 'url':
                    try:
                        valid_kws.add(_extract_domain_kw(path))
                    except: pass
                elif t in ['exe', 'app'] or not t:
                    valid_kws.add(base.replace(".exe", ""))
            
            # Extract URLs from cmd if any
            if c:
                for chunk in c.split(TAB_SEPARATOR.lower()):
                    c_clean = chunk.strip()
                    if c_clean:
                        if c_clean.startswith("http"):
                            try:
                                valid_kws.add(_extract_domain_kw(c_clean))
                            except: pass
                        else:
                            valid_paths.add(c_clean)
            
            if t == 'url':
                b_cmd = item.get('browser', '').lower()
                if b_cmd and b_cmd != 'default':
                    b_exe = os.path.basename(b_cmd).replace(".exe", "")
                    valid_kws.add(b_exe)
                else:
                    valid_kws.update(["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"])
                            
        valid_kws = {k for k in valid_kws if len(k) > 2}
        print(f"\n[Recover] Iniciando. Buscando estas rutinas {valid_paths}")
        print(f"[Recover] Buscando estas Palabras Clave: {valid_kws}\n")
        
        def _task():
            try:
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
                    
                    # We might skip typical overlay windows or taskbar
                    cls_name = win32gui.GetClassName(hwnd)
                    if cls_name in ["Progman", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow"]: return
                    
                    # Filter by configured paths
                    process_path = _get_process_path(hwnd)
                    win_title = win32gui.GetWindowText(hwnd).lower()
                    
                    is_valid = False
                    match_reason = ""
                    for vp in valid_paths:
                        if vp in process_path or vp in win_title:   
                            is_valid = True
                            match_reason = f"Ruta ({vp})"
                            break
                    
                    if not is_valid:
                        for kw in valid_kws:
                            if kw in win_title or kw in process_path:
                                is_valid = True
                                match_reason = f"Keyword ({kw})"
                                break

                    if not is_valid: 
                        print(f"  [IGNORADA] {win_title[:40]}... (No coincide con config)")
                        return

                    key = self._detect_zone_for_window(hwnd)
                    if key:
                        if key not in self.zone_stacks:
                            self.zone_stacks[key] = []
                        if hwnd not in self.zone_stacks[key]:
                            self.zone_stacks[key].append(hwnd)
                        print(f"  [RECUPERADA] -> '{win_title[:30]}...' coincidio por: {match_reason}")
                    else:
                        print(f"  [SIN ZONA FZ] -> '{win_title[:30]}...' coincidio pero NO esta encajada en una zona.")
                            
                win32gui.EnumWindows(_enum_cb, None)
                
                count = sum(len(v) for v in self.zone_stacks.values())
                print(f"[Recover] Vinculadas {count} ventanas en {len(self.zone_stacks)} zonas.")
                
                self.after(0, lambda: self._on_recover_done(count))
            except Exception as e:
                print(f"Error recuperando entorno: {e}")
                self.after(0, lambda: self.btn_recover.configure(state="normal"))
                
        threading.Thread(target=_task, daemon=True).start()

    def _on_recover_done(self, count):
        self.btn_recover.configure(state="normal")
        from tkinter import messagebox
        messagebox.showinfo("Recuperación completada", f"Se han detectado y emparejado {count} ventanas pertenecientes a tu categoría activa ('{self.current_category}').\n\nLos atajos de rotación ya vuelven a estar operativos sobre ellas.", parent=self)

    def show_recover_info(self):
        from tkinter import messagebox
        messagebox.showinfo("¿Qué hace Recuperar Info?", 
                            "Si has cerrado el launcher accidentalmente, o tenías ventanas abiertas manualmente, esta función las escanea y las vincula con las zonas de la configuración activa.\n\n"
                            "⚠️ SOLO detectará las aplicaciones que estén registradas en la categoría que tengas seleccionada actualmente.", parent=self)


    def launch_workspace(self):
        items_to_launch = self.apps_data.get(self.current_category, [])
        if not items_to_launch: return

        self.load_fancyzones_layouts()
        self.btn_launch.configure(state="disabled", text="⏳ LANZANDO ENTORNO...")

        def _launch_task():
            import win32api, win32gui, copy, time
            desk_guids = []
            if WINDOWS_LIBS_AVAILABLE:
                try:
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
                            win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, z_l, z_t, z_w, z_h, win32con.SWP_SHOWWINDOW)
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
            # Normalizar el virtual-desktop-id del entry para comparar
            e_d_guid = _norm_id(entry.get("virtual-desktop-id", ""))
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

            for mi in monitors_info:
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
            # Snapshot Matemático Directo: Eliminadas compensaciones fantasma
            win32gui.SetWindowPos(matched_hwnd, win32con.HWND_TOP, z_l, z_t, z_w, z_h, win32con.SWP_SHOWWINDOW)
            
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

if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()