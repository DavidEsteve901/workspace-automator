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
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Añadir Multi-Web")
        self.geometry("500x400")
        self.result = None
        self.transient(parent)
        self.grab_set()
        
        ctk.CTkLabel(self, text="URLs para este grupo (Multi-pestaña):", font=("Roboto", 14, "bold")).pack(anchor="w", padx=20, pady=(20, 10))
        
        self.tabs_scroll = ctk.CTkScrollableFrame(self, height=200)
        self.tabs_scroll.pack(fill="both", expand=True, padx=20, pady=5)
        
        ctk.CTkButton(self, text="➕ Añadir URL", command=self.add_tab_entry, fg_color="#4B4B4B", hover_color="#333").pack(pady=10)
        
        self.tab_entries = []
        self.add_tab_entry("https://google.com")
        
        self.btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=(10, 15))
        ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#555", command=self.destroy, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.btn_frame, text="Guardar Listado", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=140).pack(side="right")

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
            "cmd": f" {TAB_SEPARATOR} ".join(urls)
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

    def load_data(self):
        if not os.path.exists(self.applied_path):
            ctk.CTkLabel(self.scroll, text="No se encontró applied-layouts.json").pack(pady=20)
            return
            
        try:
            with open(self.applied_path, 'r', encoding='utf-8') as f:
                self.applied_data = json.load(f)
                
            layouts_list = self.applied_data.get("applied-layouts", [])
            
            # Mapear GUIDs de escritorios visuales reales
            desk_names_map = {}
            if WINDOWS_LIBS_AVAILABLE:
                try:
                    for i, d in enumerate(get_virtual_desktops()):
                        g = str(d.id).upper()
                        if not g.startswith("{"): g = "{" + g + "}"
                        desk_names_map[g] = d.name if d.name else f"Escritorio {i+1}"
                except: pass
            
            for al in layouts_list:
                dev = al.get("device", {})
                mon_str = dev.get("monitor", "Unk")
                mon_num = dev.get("monitor-number", "?")
                vd_guid = dev.get("virtual-desktop", "?")
                
                # Limpiar texto del monitor para que sea legible
                clean_mon = mon_str.replace("\\\\.\\", "").replace("DISPLAY", "Display ")
                if clean_mon == mon_str and "LOCALDISPLAY" in mon_str: clean_mon = "Display Principal"
                
                # Intentar mapear el GUID del escritorio al real
                vd_name = desk_names_map.get(vd_guid.upper())
                if not vd_name: 
                    # Fallback si por alguna rareza el GUID no está activo actualmente
                    vd_name = f"Virtual D. ({vd_guid[:8]})"
                
                app_lay = al.get("applied-layout", {})
                curr_uuid = app_lay.get("uuid", "")
                curr_name = self.uuid_to_name.get(curr_uuid, "Desconocido/Priority Grid")
                
                row = ctk.CTkFrame(self.scroll, fg_color="#333", corner_radius=5)
                row.pack(fill="x", pady=4, padx=5)
                
                lbl = ctk.CTkLabel(row, text=f"📺 {clean_mon}  |  🖥️ {vd_name}", anchor="w", font=("Roboto", 13))
                lbl.pack(side="left", padx=10, fill="x", expand=True)

                available_opts = list(self.name_to_uuid.keys())
                if curr_name not in available_opts and curr_name != "Desconocido/Priority Grid":
                    available_opts.append(curr_name)
                    
                var = ctk.StringVar(value=curr_name if curr_name in available_opts else (available_opts[0] if available_opts else ""))
                combo = ctk.CTkComboBox(row, values=available_opts, variable=var, width=180)
                combo.pack(side="right", padx=10, pady=5)
                
                # Guardamos la referencia para poder editar el dict después
                self.combos_map.append((app_lay, var))
                
        except Exception as e:
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
            
        self.destroy()

    def cancel(self):
        self.destroy()

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
        
        # Opciones dinámicas de escritorio
        self.desk_var = ctk.StringVar(value="Escritorio Actual")
        self.desk_combo = ctk.CTkComboBox(self.top_frame, variable=self.desk_var, values=["Escritorio Actual"], width=140)
        self.desk_combo.pack(side="left", padx=5)
        
        ctk.CTkButton(self.top_frame, text="Sel. Escritorio", width=100, command=self.select_chosen_desktop).pack(side="left", padx=5)
        
        ctk.CTkButton(self.top_frame, text="Todos", width=80, command=self.select_all).pack(side="left", padx=5)
        ctk.CTkButton(self.top_frame, text="Ninguno", width=80, command=self.select_none).pack(side="left", padx=5)

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
            if not p: continue
            
            paths.append(os.path.normpath(p).lower())
            
            base = os.path.basename(p)
            if t == 'vscode':
                kws.append(base.lower())
                kws.append("visual studio code")
            elif t == 'ide':
                kws.append(base.lower())
            elif t == 'obsidian':
                kws.append(base.lower())
                kws.append("obsidian")
            elif t == 'powershell':
                kws.append(base.lower())
                kws.append("terminal")
                kws.append("powershell")
            elif t == 'url':
                try:
                    domain = p.replace("https://", "").replace("http://", "").split("/")[0]
                    kws.append(domain.lower())
                except: pass
            elif t == 'exe' or t == 'app' or not t:
                kws.append(base.replace(".exe", "").lower())
                
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
        
        def enum_windows_proc(hwnd, lParam):
            if win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd):
                title = win32gui.GetWindowText(hwnd)
                if not title: return
                if title == "Program Manager": return
                
                # Excluir esta app y algunas cosas de sistema
                if "Dev Workspace Automator V9" in title or "Limpiar Entorno" in title: return
                if title == "Settings" or title == "Configuración": return
                
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
                if p_path and p_path in conf_paths:
                    matched = True
                if not matched and any(kw in t_lower for kw in kws):
                    matched = True
                
                self.windows_data.append({
                    "hwnd": hwnd,
                    "title": title,
                    "desktop_id": desk_id,
                    "desktop_name": desk_name,
                    "kw_match": matched,
                    "var": ctk.BooleanVar(value=False)
                })

        win32gui.EnumWindows(enum_windows_proc, 0)
        self.windows_data.sort(key=lambda x: (not x["kw_match"], x["desktop_name"], x["title"]))

    def populate_list(self):
        for w in self.scroll_frame.winfo_children(): w.destroy()
            
        for wdata in self.windows_data:
            row = ctk.CTkFrame(self.scroll_frame, fg_color="transparent")
            row.pack(fill="x", pady=2)
            
            chk = ctk.CTkCheckBox(row, text="", variable=wdata["var"], width=30)
            chk.pack(side="left", padx=5)
            
            lbl_title = ctk.CTkLabel(row, text=wdata["title"], anchor="w")
            lbl_title.pack(side="left", padx=5, fill="x", expand=True)
            
            lbl_desk = ctk.CTkLabel(row, text=f"[{wdata['desktop_name']}]", width=120, text_color="gray")
            lbl_desk.pack(side="right", padx=5)

            if wdata["kw_match"]:
                lbl_title.configure(text_color="#2CC985") # Resaltar las que coinciden con la config
                chk.select()

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
        count = 0
        for w in self.windows_data:
            if w["var"].get():
                try:
                    win32gui.PostMessage(w["hwnd"], win32con.WM_CLOSE, 0, 0)
                    count += 1
                except Exception as e:
                    print(f"Error cerrando {w['title']}: {e}")
        
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
        
        self.btn_launch = ctk.CTkButton(self.action_frame, text="🚀 LANZAR ENTORNO", height=50, font=("Roboto", 18, "bold"), 
                                        fg_color="#2CC985", hover_color="#24A36B", command=self.launch_workspace)
        self.btn_launch.pack(side="left", fill="x", expand=True, padx=(0, 5))
        
        self.btn_clean_bottom = ctk.CTkButton(self.action_frame, text="🧹 LIMPIAR ENTORNO", height=50, width=200, font=("Roboto", 16, "bold"), 
                                       fg_color="#AA0000", hover_color="#770000", command=self.open_cleaner)
        self.btn_clean_bottom.pack(side="right", fill="x", padx=(5, 0))

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
                    layout_uuid = layout.get("uuid", "").upper()
                    
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
                        uuid = al.get("uuid", "").upper()
                        
                        if uuid and uuid != "{00000000-0000-0000-0000-000000000000}":
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
                tag, col, txt = "[WEB]", "#E5A00D", f"Web ({num_tabs} pestañas): {p}"
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
        try:
            if win32gui.IsIconic(hwnd): win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        except: pass

        try:
            fg = win32gui.GetForegroundWindow()
            current_tid = win32api.GetCurrentThreadId()
            fg_tid = win32process.GetWindowThreadProcessId(fg)[0] if fg else 0
            target_tid = win32process.GetWindowThreadProcessId(hwnd)[0]

            user32 = ctypes.windll.user32
            if fg_tid and fg_tid != current_tid: user32.AttachThreadInput(fg_tid, current_tid, True)
            if target_tid and target_tid != current_tid: user32.AttachThreadInput(target_tid, current_tid, True)

            win32gui.SetForegroundWindow(hwnd)
            win32gui.BringWindowToTop(hwnd)
            win32gui.SetActiveWindow(hwnd)
            
            # Forzar posición Z al frente absoluto sin alterar posición ni tamaño
            win32gui.SetWindowPos(hwnd, win32con.HWND_TOP, 0, 0, 0, 0, 
                                  win32con.SWP_NOMOVE | win32con.SWP_NOSIZE | win32con.SWP_SHOWWINDOW)

            if fg_tid and fg_tid != current_tid: user32.AttachThreadInput(fg_tid, current_tid, False)
            if target_tid and target_tid != current_tid: user32.AttachThreadInput(target_tid, current_tid, False)

            return True
        except Exception as e:
            print("Foreground error:", e)
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

                def on_click(x, y, button, pressed):
                    if pressed:
                        btn_name = button.name # 'left', 'right', 'x1', etc.
                        for key_id, func in actions.items():
                            combo = hk.get(key_id)
                            if combo and is_mouse_combo(combo):
                                if match_mouse_hotkey(combo, btn_name):
                                    func()
                    return True

                # Iniciar listenes de Pynput
                h = keyboard.GlobalHotKeys(kb_mapping)
                h.start()
                
                ml = mouse.Listener(on_click=on_click)
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
        
        # También probamos con la ventana bajo el ratón por si acaso el clic se hace
        # sobre una ventana que aún no es la activa según el sistema operativo
        pos = win32api.GetCursorPos()
        try:
            mouse_hwnd = win32gui.WindowFromPoint(pos)
        except:
            mouse_hwnd = None
            
        # Generamos una lista de candidatos (foco + ratón + sus ancestros raíz)
        candidates = []
        if fg_hwnd: candidates.append(fg_hwnd)
        if mouse_hwnd and mouse_hwnd != fg_hwnd: candidates.append(mouse_hwnd)
        
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
        
        # Buscar en nuestros stacks cuál de estas corresponde a una zona activa
        for h in check_list:
            for key, stack in self.zone_stacks.items():
                if h in stack:
                    target_key = key
                    found_target_hwnd = h
                    break
            if target_key: break
            
        if not target_key: return fg_hwnd, None, None
        
        # Purgar ventanas muertas
        stack = [h for h in self.zone_stacks[target_key] if win32gui.IsWindow(h)]
        self.zone_stacks[target_key] = stack
        
        return found_target_hwnd, target_key, stack

    def _cycle_zone_forward(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 1:
            try:
                # Si la ventana actual está en el stack, vamos a la siguiente
                if fg in stack:
                    idx = stack.index(fg)
                    next_idx = (idx + 1) % len(stack)
                else:
                    # Si por algún motivo se perdió el rastro pero estamos en la zona, empezamos por la primera
                    next_idx = 0
                
                self._force_foreground(stack[next_idx])
            except Exception as e:
                print(f"Error en ciclo forward: {e}")

    def _cycle_zone_backward(self):
        fg, key, stack = self._get_active_zone_context()
        if stack and len(stack) > 1:
            try:
                if fg in stack:
                    idx = stack.index(fg)
                    next_idx = (idx - 1) % len(stack)
                else:
                    next_idx = len(stack) - 1
                
                self._force_foreground(stack[next_idx])
            except Exception as e:
                print(f"Error en ciclo backward: {e}")
            
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
                
            monitors_info = []
            try:
                for idx, (hMonitor, _, pyRect) in enumerate(win32api.EnumDisplayMonitors()):
                    minfo = win32api.GetMonitorInfo(hMonitor)
                    monitors_info.append({
                        "device": minfo.get("Device", f"\\\\.\\DISPLAY{idx+1}"),
                        "work_area": minfo.get("Work"),
                        "bounds": pyRect,
                        "enum_idx": idx
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
                    
                m_dev = monitors_info[0]["device"] if monitors_info else "\\\\.\\DISPLAY1"
                m_eidx = 0
                if mon.startswith("Pantalla "):
                    try: 
                        m_idx = int(mon.split(" ")[1]) - 1
                        if 0 <= m_idx < len(monitors_info): 
                            m_dev = monitors_info[m_idx]["device"]
                            m_eidx = monitors_info[m_idx]["enum_idx"]
                    except: pass
                elif mon.startswith("Monitor "):
                    try: 
                        m_idx = int(mon.replace("Monitor ", "")) - 1
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
                intent["_d_guid"] = d_guid
                intent["_m_dev"] = m_dev
                intent["_m_eidx"] = m_eidx
                intent["_l_uuid"] = layout_uuid
                intent["_z_idx"] = zone_idx
                intents.append(intent)

            from collections import defaultdict
            groups = defaultdict(lambda: defaultdict(list))
            for intt in intents:
                groups[intt["_d_guid"]][intt["_m_dev"]].append(intt)

            for dguid in groups:
                if dguid and WINDOWS_LIBS_AVAILABLE:
                    try:
                        target_d = next((d for d in get_virtual_desktops() if str(d.id) == str(dguid)), None)
                        if target_d:
                            target_d.go()
                            if not self._wait_for_condition(lambda: str(VirtualDesktop.current().id) == str(dguid), timeout=4.0):
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

                    for intt in cur_items:
                        self._launch_and_snap_intent(intt, monitors_info)

            self.after(0, lambda: self.btn_launch.configure(state="normal", text="🚀 LANZAR ENTORNO"))

        import threading
        threading.Thread(target=_launch_task, daemon=True).start()

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
                cmds_raw = intent.get('cmd', '')
                if cmds_raw:
                    for u in cmds_raw.split(TAB_SEPARATOR):
                        cu = u.strip()
                        if cu:
                            subprocess.Popen(f'start msedge --new-window "{cu}"', shell=True)
                            time.sleep(0.5)
                else:
                    subprocess.Popen(f'start msedge --new-window "{p}"', shell=True)
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
            z_key = (intent["_d_guid"], intent["_m_dev"], intent["_l_uuid"], intent["_z_idx"])
            if z_key not in self.zone_stacks:
                self.zone_stacks[z_key] = []
            
            # Guardamos la HWND al final de la pila (Stack de Render/Rotación)
            if matched_hwnd not in self.zone_stacks[z_key]:
                self.zone_stacks[z_key].append(matched_hwnd)
                
        else:
            # Fallback a centrado si no hay layout (o error FZ)
            cen_x, cen_y = l + (r-l)//2, t_y + (b-t_y)//2
            win32gui.SetWindowPos(matched_hwnd, win32con.HWND_TOP, cen_x - 400, cen_y - 300, 800, 600, win32con.SWP_SHOWWINDOW)

if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()