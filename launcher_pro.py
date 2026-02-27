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

# ─────────────────────────────────────────────────────────────────────────────
#  APP PRINCIPAL
# ─────────────────────────────────────────────────────────────────────────────
class DevLauncherApp(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("Dev Workspace Automator V9 - Windows Terminal Tabs")
        self.geometry("900x750")
        
        self.db_file = os.path.join(APP_DIR, "mis_apps_config_v2.json")
        self.current_category = None
        self.last_saved_category = None
        self.fancyzones_path = os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\PowerToys\FancyZones")
        self.available_layouts = {}
        self.available_monitors = ["Por defecto"]
        self.default_layout_name = None
        self.applied_mappings = {} # Almacena GUID_Desktop_MonitorFriendly -> Nombre de Layout de FZ
        self.apps_data = self.load_data()
        self.load_fancyzones_layouts()

        # --- HEADER ---
        self.header_frame = ctk.CTkFrame(self)
        self.header_frame.pack(fill="x", padx=20, pady=(20, 10))
        
        ctk.CTkLabel(self.header_frame, text="Modo:", font=("Roboto", 16, "bold")).pack(side="left", padx=10)
        self.category_option = ctk.CTkOptionMenu(self.header_frame, values=[], command=self.change_category, width=200)
        self.category_option.pack(side="left", padx=5)
        
        ctk.CTkButton(self.header_frame, text="Renombrar", width=80, fg_color="#555", command=self.rename_category_dialog).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="Duplicar", width=80, fg_color="#555", command=self.duplicate_category_dialog).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="🗑️", width=40, fg_color="#AA0000", hover_color="#770000", command=self.delete_category).pack(side="left", padx=5)
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
                      fg_color="#4B4B4B", hover_color="#333", command=self.open_assign_layouts_dialog).pack(side="right", padx=10)

        # --- LANZAR Y LIMPIAR ---
        self.action_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.action_frame.pack(fill="x", padx=20, pady=10)
        
        self.btn_launch = ctk.CTkButton(self.action_frame, text="🚀 LANZAR ENTORNO", height=50, font=("Roboto", 18, "bold"), 
                                        fg_color="#2CC985", hover_color="#24A36B", command=self.launch_workspace)
        self.btn_launch.pack(side="left", fill="x", expand=True, padx=(0, 5))
        
        self.btn_clean = ctk.CTkButton(self.action_frame, text="🧹 LIMPIAR ENTORNO", height=50, width=200, font=("Roboto", 16, "bold"), 
                                       fg_color="#AA0000", hover_color="#770000", command=self.open_clean_dialog)
        self.btn_clean.pack(side="right", fill="x", padx=(5, 0))

        # --- LISTA ---
        self.apps_frame = ctk.CTkScrollableFrame(self, label_text="Elementos configurados")
        self.apps_frame.pack(fill="both", expand=True, padx=20, pady=10)

        # --- FOOTER ---
        self.footer_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.footer_frame.pack(fill="x", padx=20, pady=20)
        
        ctk.CTkButton(self.footer_frame, text="Añadir .EXE", width=90, command=self.add_exe).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Web", width=70, fg_color="#E5A00D", hover_color="#B57B02", command=self.add_url).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="IDE", width=90, fg_color="#007ACC", hover_color="#005A9E", command=self.add_ide_project).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Obsidian", width=90, fg_color="#7A3EE8", hover_color="#5D24B8", command=self.add_obsidian_vault).pack(side="left", padx=5, expand=True, fill="x")
        ctk.CTkButton(self.footer_frame, text="Terminal (Tabs)", width=110, fg_color="#5A5A5A", hover_color="#333", command=self.add_powershell).pack(side="left", padx=5, expand=True, fill="x")

        self.refresh_categories()

    # --- DATOS ---
    def load_data(self):
        if os.path.exists(self.db_file):
            try:
                with open(self.db_file, "r") as f:
                    data = json.load(f)
                    if "categories_data" in data and "settings" in data:
                        self.last_saved_category = data["settings"].get("last_category")
                        if "fancyzones_path" in data["settings"]:
                            self.fancyzones_path = data["settings"]["fancyzones_path"]
                        return data["categories_data"]
                    return data
            except: return {}
        return {}

    def save_data(self):
        full_data = {
            "settings": { 
                "last_category": self.current_category,
                "fancyzones_path": self.fancyzones_path
            },
            "categories_data": self.apps_data
        }
        try:
            with open(self.db_file, "w") as f: json.dump(full_data, f, indent=4)
        except Exception as e: messagebox.showerror("Error", str(e))

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

    def open_assign_layouts_dialog(self):
        dlg = AssignLayoutsDialog(self)
        self.wait_window(dlg)
        self.load_fancyzones_layouts()

    # --- CATEGORÍAS ---
    def refresh_categories(self):
        cats = list(self.apps_data.keys())
        if not cats:
            self.apps_data["General"] = []
            cats = ["General"]
            self.save_data()
        
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
        self.save_data()
        self.refresh_apps_list()

    def add_category_dialog(self):
        if n := ctk.CTkInputDialog(text="Nombre:", title="Nueva").get_input():
            if n not in self.apps_data:
                self.apps_data[n] = []
                self.current_category = n
                self.save_data()
                self.refresh_categories()

    def rename_category_dialog(self):
        if not self.current_category: return
        if n := ctk.CTkInputDialog(text=f"Nuevo nombre:", title="Renombrar").get_input():
            if n not in self.apps_data:
                self.apps_data[n] = self.apps_data.pop(self.current_category)
                self.current_category = n
                self.save_data()
                self.refresh_categories()

    def duplicate_category_dialog(self):
        if not self.current_category: return
        n = ctk.CTkInputDialog(text=f"Nombre para la copia de '{self.current_category}':", title="Duplicar").get_input()
        if n:
            if n not in self.apps_data:
                # Creamos una copia profunda de los elementos actuales
                self.apps_data[n] = json.loads(json.dumps(self.apps_data[self.current_category]))
                self.current_category = n
                self.save_data()
                self.refresh_categories()
            else:
                messagebox.showerror("Error", "Ya existe una categoría con ese nombre.")

    def delete_category(self):
        if self.current_category and messagebox.askyesno("Borrar", f"¿Eliminar '{self.current_category}'?"):
            del self.apps_data[self.current_category]
            self.current_category = None
            self.save_data()
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
            self.save_data()
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
            self.save_data()
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
                self.save_data()
                self.refresh_apps_list()

    def add_item(self, t, p, extras=None):
        item = {"type": t, "path": p}
        if extras:
            item.update(extras)
        self.apps_data[self.current_category].append(item)
        self.save_data()
        self.refresh_apps_list()
        
    def remove_item(self, idx):
        del self.apps_data[self.current_category][idx]
        self.save_data()
        self.refresh_apps_list()

    # --- LANZAMIENTO AVANZADO Y LIMPIEZA ---
    def open_clean_dialog(self):
        if not WINDOWS_LIBS_AVAILABLE:
            messagebox.showwarning("Error", "No se detectaron las librerías necesarias de Windows (win32gui, etc).")
            return
        dlg = CleanWorkspaceDialog(self)
        self.wait_window(dlg)

    def launch_workspace(self):
        items_to_launch = self.apps_data.get(self.current_category, [])
        if not items_to_launch: return

        # Cambiar el botón visualmente
        self.btn_launch.configure(state="disabled", text="⏳ LANZANDO ENTORNO...")

        def _launch_task():
            for item in items_to_launch:
                try:
                    t = item.get('type')
                    p = item.get('path')
                    desktop = item.get('desktop', 'Por defecto')
                    monitor = item.get('monitor', 'Por defecto')
                    zone = item.get('fancyzone', 'Ninguna')
                    delay_str = item.get('delay', '0')
                    
                    try: delay_s = float(delay_str)
                    except: delay_s = 0.0
                    
                    desk_num = None
                    if desktop.startswith("Escritorio "):
                        try: desk_num = int(desktop.split(" ")[1])
                        except: pass
                        
                    target_mon_idx = None
                    if monitor.startswith("Pantalla "):
                        try: target_mon_idx = int(monitor.split(" ")[1]) - 1
                        except: pass
                    elif monitor.startswith("Monitor "):
                        try: target_mon_idx = int(monitor.replace("Monitor ", "")) - 1
                        except: pass
                    
                    if delay_s > 0:
                        time.sleep(delay_s)
                        
                    # [CRÍTICO] Forzar el salto de escritorio ANTES de lanzar el proceso
                    if desk_num is not None and WINDOWS_LIBS_AVAILABLE:
                        try:
                            desktops = get_virtual_desktops()
                            if 1 <= desk_num <= len(desktops):
                                target_desktop = desktops[desk_num - 1]
                                target_desktop.go() # Te transporta fisicamente a ese escritorio
                                time.sleep(0.8) # Darle respiro seguro de animacion a Windows al cambiar de escritorio
                        except Exception as e:
                            print(f"Error cambiando escritorio antes de lanzar: {e}")
                    
                    def _launch_and_position():
                        process = None
                        if t == 'url': 
                            if WINDOWS_LIBS_AVAILABLE:
                                # Inyectar para navegadores más comunes
                                self._inject_fancyzones_history(["msedge.exe", "chrome.exe", "firefox.exe", "brave.exe"], desk_num, target_mon_idx, zone)
                            
                            # Soporte multi-pestaña para URL
                            cmds_raw = item.get('cmd', '')
                            if cmds_raw:
                                urls = cmds_raw.split(TAB_SEPARATOR)
                                first = True
                                for u in urls:
                                    clean_u = u.strip()
                                    if clean_u:
                                        if first:
                                            webbrowser.open_new(clean_u)
                                            time.sleep(1.0) # Dar margen para que la nueva ventana se abra y tome foco
                                            first = False
                                        else:
                                            webbrowser.open_new_tab(clean_u)
                                            time.sleep(0.2)
                            else:
                                webbrowser.open_new(p)
                        elif t == 'vscode': 
                            if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history("code.exe", desk_num, target_mon_idx, zone)
                            process = subprocess.Popen(f'code "{p}"', shell=True)
                        elif t == 'ide': 
                            ide_cmd = item.get('ide_cmd', 'code')
                            exe_hint = f"{ide_cmd}.exe" if not ide_cmd.endswith(".exe") else ide_cmd
                            if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history(exe_hint, desk_num, target_mon_idx, zone)
                            process = subprocess.Popen(f'{ide_cmd} "{p}"', shell=True)
                        elif t == 'exe': 
                            exe_hint = os.path.normpath(p)
                            if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history(exe_hint, desk_num, target_mon_idx, zone)
                            process = subprocess.Popen(p)
                        elif t == 'obsidian':
                            if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history("obsidian.exe", desk_num, target_mon_idx, zone)
                            encoded = urllib.parse.quote(p)
                            webbrowser.open(f"obsidian://open?path={encoded}")
                        elif t == 'powershell':
                            cmds_raw = item.get('cmd', '')
                            tabs_content = cmds_raw.split(TAB_SEPARATOR)
                            
                            if shutil.which("wt") is None:
                                simple_cmd = tabs_content[0].replace(";", " & ")
                                if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history("powershell.exe", desk_num, target_mon_idx, zone)
                                process = subprocess.Popen(f'start powershell -NoExit -Command "Set-Location \'{p}\'; {simple_cmd}"', shell=True)
                            else:
                                if WINDOWS_LIBS_AVAILABLE: self._inject_fancyzones_history("WindowsTerminal.exe", desk_num, target_mon_idx, zone)
                                
                                # Lanzar todas las pestañas dentro de una MISMA ventana de Windows Terminal 
                                wt_args = ["wt", "-w", "-1", "-d", p, "powershell", "-NoExit"]
                                
                                first_cmd = tabs_content[0].strip()
                                if first_cmd:
                                    wt_args.extend(["-Command", first_cmd])
                                    
                                for content in tabs_content[1:]:
                                    wt_args.append(";")
                                    wt_args.extend(["new-tab", "-d", p, "powershell", "-NoExit"])
                                    clean_cmds = content.strip()
                                    if clean_cmds:
                                        wt_args.extend(["-Command", clean_cmds])
                                
                                subprocess.Popen(wt_args)
    
                    _launch_and_position()
                    
                    # Pausa general post-lanzamiento para que Windows y PowerToys asimilen el cambio
                    time.sleep(3.0)
    
                except Exception as e:
                    print(f"Error lanzando {item.get('type')}: {e}")
            
            # Restaurar el botón desde otro hilo
            self.after(0, lambda: self.btn_launch.configure(state="normal", text="🚀 LANZAR ENTORNO"))

        threading.Thread(target=_launch_task, daemon=True).start()

    def _get_process_path(self, hwnd):
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

    def _inject_fancyzones_history(self, app_exes, desk_num, target_mon_idx, zone_name):
        if isinstance(app_exes, str):
            app_exes = [app_exes]
            
        if not WINDOWS_LIBS_AVAILABLE or not self.fancyzones_path or zone_name == 'Ninguna': return
        
        parts = zone_name.rsplit(" - Zona ", 1)
        if len(parts) != 2: return
        layout_name = parts[0]
        try: zone_idx = int(parts[1].split()[0]) - 1
        except: return
        if zone_idx < 0: return

        # 1. Hallar UUID real del layout configurado en el Launcher
        target_uuid = None
        for lname, info in self.available_layouts.items():
            if lname == layout_name:
                target_uuid = info.get("uuid")
                break
        if not target_uuid: return

        # 2. Hallar GUID del escritorio real
        target_guid = None
        if desk_num is not None:
            try:
                vds = get_virtual_desktops()
                if 1 <= desk_num <= len(vds):
                    target_guid = str(vds[desk_num - 1].id).upper()
                    if not target_guid.startswith("{"): target_guid = "{" + target_guid + "}"
            except: pass
            
        # 3. Hallar Monitor Number nativo de FZ (1-indexed based on EnumDisplayMonitors logic FZ uses)
        monito_number = 1
        if target_mon_idx is not None: monito_number = target_mon_idx + 1

        # 4. Emparejar ALL dispositivos asociados a ese escritorio y monitor
        # FancyZones guarda fantasmas de monitores viejos, extraemos todos para inyectarlos en tromba
        applied_json_path = os.path.join(self.fancyzones_path, "applied-layouts.json")
        target_devices = []
        
        if os.path.exists(applied_json_path):
            try:
                with open(applied_json_path, 'r', encoding='utf-8') as f:
                    app_data = json.load(f)
                    
                for al in app_data.get("applied-layouts", []):
                    dev = al.get("device", {})
                    vd_match = True
                    if target_guid:
                        vd_match = (dev.get("virtual-desktop", "").upper() == target_guid.upper())
                    
                    mon_match = (dev.get("monitor-number", 1) == monito_number)
                    
                    if vd_match and mon_match:
                        target_devices.append(dev)
            except Exception as e:
                print("Error applied-layouts:", e)

        if not target_devices:
            # Fallback manual de device si FZ lo ha borrado
            target_devices.append({
                "monitor": "FallbackMonitor",
                "monitor-number": monito_number,
                "virtual-desktop": target_guid if target_guid else ""
            })

        # 5. Inyectar o crear en app-zone-history.json
        history_path = os.path.join(self.fancyzones_path, "app-zone-history.json")
        hdata = {"app-zone-history": []}
        
        for retry in range(10):
            try:
                if os.path.exists(history_path):
                    with open(history_path, 'r', encoding='utf-8') as f:
                        hdata = json.load(f)
                break
            except Exception: 
                time.sleep(0.2)

        app_list = hdata.get("app-zone-history", [])
        
        def resolve_app_path(name):
            if os.path.isabs(name): return os.path.normpath(name)
            if name.lower() in ("windowsterminal.exe", "wt.exe"):
                try:
                    out = subprocess.check_output(['powershell', '-NoProfile', '-Command', '(Get-AppxPackage Microsoft.WindowsTerminal).InstallLocation'], timeout=3, text=True, creationflags=0x08000000).strip()
                    if out: return os.path.join(out, "WindowsTerminal.exe")
                except: pass
            import winreg
            try:
                for root in [winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER]:
                    try:
                        with winreg.OpenKey(root, rf"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}", 0, winreg.KEY_READ) as key:
                            val, _ = winreg.QueryValueEx(key, "")
                            if val: return os.path.normpath(val)
                    except WindowsError: pass
                    if not name.lower().endswith(".exe"):
                        try:
                            with winreg.OpenKey(root, rf"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}.exe", 0, winreg.KEY_READ) as key:
                                val, _ = winreg.QueryValueEx(key, "")
                                if val: return os.path.normpath(val)
                        except WindowsError: pass
            except: pass
            w = shutil.which(name)
            if w: return os.path.normpath(w)
            return name
        
        for app_exe in app_exes:
            # Encontrar el ABSOLUTE PATH del app, FZ requiere la ruta real o no hace match
            exact_exe = resolve_app_path(app_exe)
            target_app_entry = None
            
            for entry in app_list:
                if exact_exe.lower() == entry.get("app-path", "").lower():
                    exact_exe = entry["app-path"]
                    target_app_entry = entry
                    break
                    
            if not target_app_entry:
                target_app_entry = {"app-path": exact_exe, "history": []}
                app_list.append(target_app_entry)
                
            history_arr = target_app_entry.get("history", [])
            
            # Buscar si ya tiene un historial para este zoneset-uuid / device virtual desktop
            for t_dev in target_devices:
                updated_history = False
                for h in history_arr:
                    h_dev = h.get("device", {})
                    # Matchear si el FZ history record aplica a nuestro layout UUID, y coincide con Virtual Desktop y num monitor
                    if (h.get("zoneset-uuid") == target_uuid and 
                        h_dev.get("virtual-desktop", "").upper() == target_guid and 
                        h_dev.get("monitor-number", 1) == monito_number and
                        h_dev.get("monitor", "") == t_dev.get("monitor", "")):
                        
                        h["zone-index-set"] = [zone_idx]
                        h["device"] = t_dev
                        updated_history = True
                        break
                        
                if not updated_history:
                    history_arr.append({
                        "zone-index-set": [zone_idx],
                        "device": t_dev,
                        "zoneset-uuid": target_uuid
                    })
            
        hdata["app-zone-history"] = app_list
        
        for retry in range(10):
            try:
                with open(history_path, 'w', encoding='utf-8') as f:
                    json.dump(hdata, f)
                break
            except Exception: 
                time.sleep(0.2)

if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()