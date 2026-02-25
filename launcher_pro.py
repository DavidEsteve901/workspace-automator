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

# ─────────────────────────────────────────────────────────────────────────────
#  EDITOR DE COMANDOS (PowerShell + Tabs)
# ─────────────────────────────────────────────────────────────────────────────
class CommandEditorDialog(ctk.CTkToplevel):
    def __init__(self, parent, title="Editor de Comandos", initial_text=""):
        super().__init__(parent)
        self.title(title)
        self.geometry("700x550")
        self.result = None
        self.transient(parent)
        self.grab_set()
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        # Header con instrucciones
        header = ctk.CTkFrame(self, fg_color="transparent")
        header.grid(row=0, column=0, padx=20, pady=(15, 5), sticky="ew")
        
        ctk.CTkLabel(header, text="Editor Multi-Pestaña", font=("Roboto", 16, "bold")).pack(side="left")
        ctk.CTkLabel(header, text="(Usa el botón para separar pestañas)", text_color="gray").pack(side="left", padx=10)

        # Área de Texto
        self.textbox = ctk.CTkTextbox(self, font=("Consolas", 13), wrap="none")
        self.textbox.grid(row=1, column=0, padx=20, pady=10, sticky="nsew")
        
        if initial_text:
            # Reemplazar ; por saltos de línea visuales
            display_text = initial_text.replace(";", "\n")
            # Asegurar que el separador se vea limpio
            display_text = display_text.replace(TAB_SEPARATOR.replace(" ", ""), TAB_SEPARATOR) 
            self.textbox.insert("0.0", display_text)

        # Botonera
        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.grid(row=2, column=0, padx=20, pady=20, sticky="ew")

        # Botón para insertar el separador
        ctk.CTkButton(btn_frame, text="➕ Insertar Nueva Pestaña", fg_color="#4B4B4B", hover_color="#333", 
                      command=self.insert_separator).pack(side="left", padx=0)

        ctk.CTkButton(btn_frame, text="Guardar y Cerrar", fg_color="#2CC985", hover_color="#24A36B", 
                      command=self.save).pack(side="right", padx=0)
        ctk.CTkButton(btn_frame, text="Cancelar", fg_color="#555", 
                      command=self.cancel).pack(side="right", padx=10)

    def insert_separator(self):
        self.textbox.insert("insert", f"\n\n{TAB_SEPARATOR}\n\n")

    def save(self):
        raw_text = self.textbox.get("0.0", "end").strip()
        if not raw_text:
            self.result = ""
            self.destroy()
            return

        # Procesamiento inteligente:
        # 1. Normalizar saltos de línea
        lines = raw_text.split('\n')
        processed_lines = []
        
        for line in lines:
            line = line.strip()
            if line == TAB_SEPARATOR:
                processed_lines.append(TAB_SEPARATOR) # Mantener marca
            elif line:
                processed_lines.append(line)
        
        # Unimos con punto y coma, PERO respetando el separador especial
        final_str = "; ".join(processed_lines)
        # El separador no debe tener punto y coma pegado
        final_str = final_str.replace(f"; {TAB_SEPARATOR};", f" {TAB_SEPARATOR} ")
        final_str = final_str.replace(f"{TAB_SEPARATOR};", f"{TAB_SEPARATOR} ")
        final_str = final_str.replace(f"; {TAB_SEPARATOR}", f" {TAB_SEPARATOR}")
        
        self.result = final_str
        self.destroy()

    def cancel(self):
        self.result = None
        self.destroy()

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

        # Seleccionar IDE
        self.ide_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.ide_frame.pack(fill="x", padx=20, pady=10)
        
        ctk.CTkLabel(self.ide_frame, text="IDE:", width=50).pack(side="left", padx=(0, 10))
        self.ide_combo = ctk.CTkComboBox(self.ide_frame, values=["code", "antigravity", "cursor", "idea64", "pycharm", "webstorm"])
        self.ide_combo.pack(side="left", fill="x", expand=True)

        # Botones
        self.btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        self.btn_frame.pack(fill="x", padx=20, pady=(10, 20))
        ctk.CTkButton(self.btn_frame, text="Cancelar", fg_color="#555", command=self.cancel, width=100).pack(side="right", padx=(10, 0))
        ctk.CTkButton(self.btn_frame, text="Guardar", fg_color="#2CC985", hover_color="#24A36B", command=self.save, width=100).pack(side="right")

        self.selected_path = None

    def browse(self):
        if p := filedialog.askdirectory(title="Carpeta del Proyecto"):
            self.selected_path = os.path.normpath(p)
            self.path_label.configure(text=os.path.basename(self.selected_path))

    def save(self):
        ide = self.ide_combo.get().strip()
        if self.selected_path and ide:
            self.result = {"path": self.selected_path, "ide_cmd": ide}
            self.destroy()
        else:
            messagebox.showwarning("Atención", "Selecciona una ruta y escribe el comando del IDE.")

    def cancel(self):
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
        self.apps_data = self.load_data()

        # --- HEADER ---
        self.header_frame = ctk.CTkFrame(self)
        self.header_frame.pack(fill="x", padx=20, pady=(20, 10))
        
        ctk.CTkLabel(self.header_frame, text="Modo:", font=("Roboto", 16, "bold")).pack(side="left", padx=10)
        self.category_option = ctk.CTkOptionMenu(self.header_frame, values=[], command=self.change_category, width=200)
        self.category_option.pack(side="left", padx=5)
        
        ctk.CTkButton(self.header_frame, text="Renombrar", width=80, fg_color="#555", command=self.rename_category_dialog).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="🗑️", width=40, fg_color="#AA0000", hover_color="#770000", command=self.delete_category).pack(side="left", padx=5)
        ctk.CTkButton(self.header_frame, text="+ Nueva", width=80, command=self.add_category_dialog).pack(side="right", padx=10)

        # --- LANZAR ---
        self.btn_launch = ctk.CTkButton(self, text="🚀 LANZAR ENTORNO", height=50, font=("Roboto", 18, "bold"), 
                                        fg_color="#2CC985", hover_color="#24A36B", command=self.launch_workspace)
        self.btn_launch.pack(fill="x", padx=20, pady=10)

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
                        return data["categories_data"]
                    return data
            except: return {}
        return {}

    def save_data(self):
        full_data = {
            "settings": { "last_category": self.current_category },
            "categories_data": self.apps_data
        }
        try:
            with open(self.db_file, "w") as f: json.dump(full_data, f, indent=4)
        except Exception as e: messagebox.showerror("Error", str(e))

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

            if t == 'url': tag, col, txt = "[WEB]", "#E5A00D", p
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
            
            if t == 'powershell':
                ctk.CTkButton(row, text="📝", width=30, fg_color="#444", command=lambda i=idx: self.edit_powershell(i)).pack(side="right", padx=2)
            
            ctk.CTkButton(row, text="X", width=30, fg_color="#FF5555", command=lambda i=idx: self.remove_item(i)).pack(side="right", padx=5)

    # --- ADDERS ---
    def add_exe(self):
        if p := filedialog.askopenfilename(filetypes=[("Exe", "*.exe")]): self.add_item("exe", os.path.normpath(p))
    def add_url(self):
        if u := ctk.CTkInputDialog(text="URL:", title="Web").get_input():
            self.add_item("url", "https://" + u if not u.startswith("http") else u)
    def add_vscode_project(self):
        if p := filedialog.askdirectory(title="Proyecto VS Code"): self.add_item("vscode", os.path.normpath(p))

    def add_ide_project(self):
        dlg = AddIDEDialog(self)
        self.wait_window(dlg)
        if dlg.result:
            self.apps_data[self.current_category].append({
                "type": "ide", 
                "path": dlg.result["path"], 
                "ide_cmd": dlg.result["ide_cmd"]
            })
            self.save_data()
            self.refresh_apps_list()
    def add_obsidian_vault(self):
        if p := filedialog.askdirectory(title="Vault Obsidian"): self.add_item("obsidian", os.path.normpath(p))

    def add_powershell(self):
        if p := filedialog.askdirectory(title="Carpeta Base para Terminal"):
            ed = CommandEditorDialog(self, title="Configurar Pestañas")
            self.wait_window(ed)
            if ed.result:
                self.apps_data[self.current_category].append({"type": "powershell", "path": os.path.normpath(p), "cmd": ed.result})
                self.save_data()
                self.refresh_apps_list()

    def edit_powershell(self, idx):
        item = self.apps_data[self.current_category][idx]
        ed = CommandEditorDialog(self, title="Editar Pestañas", initial_text=item.get('cmd',''))
        self.wait_window(ed)
        if ed.result is not None:
            item['cmd'] = ed.result
            self.save_data()
            self.refresh_apps_list()

    def add_item(self, t, p):
        self.apps_data[self.current_category].append({"type": t, "path": p})
        self.save_data()
        self.refresh_apps_list()
    def remove_item(self, idx):
        del self.apps_data[self.current_category][idx]
        self.save_data()
        self.refresh_apps_list()

    # --- LANZAMIENTO AVANZADO (WT.EXE) ---
    def launch_workspace(self):
        for item in self.apps_data.get(self.current_category, []):
            try:
                t = item.get('type')
                p = item.get('path')
                
                if t == 'url': webbrowser.open_new_tab(p)
                elif t == 'vscode': subprocess.Popen(f'code "{p}"', shell=True)
                elif t == 'ide': 
                    ide_cmd = item.get('ide_cmd', 'code')
                    subprocess.Popen(f'{ide_cmd} "{p}"', shell=True)
                elif t == 'exe': subprocess.Popen(p)
                elif t == 'obsidian':
                    encoded = urllib.parse.quote(p)
                    webbrowser.open(f"obsidian://open?path={encoded}")
                
                elif t == 'powershell':
                    cmds_raw = item.get('cmd', '')
                    # Dividir por el separador de pestañas
                    tabs_content = cmds_raw.split(TAB_SEPARATOR)
                    
                    # Verificar si 'wt' existe (Windows Terminal)
                    if shutil.which("wt") is None:
                        messagebox.showwarning("Falta Windows Terminal", "Para usar pestañas necesitas instalar 'Windows Terminal' desde la Microsoft Store.\n\nSe abrirá una ventana normal.")
                        # Fallback a ventana simple
                        simple_cmd = tabs_content[0].replace(";", " & ") # conversión básica
                        subprocess.Popen(f'start powershell -NoExit -Command "Set-Location \'{p}\'; {simple_cmd}"', shell=True)
                        continue

                    # CONSTRUCCIÓN DEL COMANDO WT (Windows Terminal)
                    # Sintaxis: wt -w 0 new-tab -d "Ruta" ... ; new-tab -d "Ruta" ...
                    wt_args = ["wt", "-w", "0"] # -w 0 usa la misma ventana si ya existe, o crea una
                    
                    for i, content in enumerate(tabs_content):
                        clean_cmds = content.strip()
                        if not clean_cmds: continue
                        
                        # Si no es la primera, añadimos separador de argumentos de wt
                        if i > 0: 
                            # Nota: wt usa ; como separador. En subprocess list se pasa como argumento suelto.
                            # Pero subprocess a veces lía los ;. La forma segura es pasar todo como string único o lista cuidadosa.
                            # Para evitar lios con subprocess y wt, pasamos argumentos separados a la lista
                            wt_args.append(";") 

                        wt_args.append("new-tab")
                        wt_args.append("-d") # Directorio de inicio
                        wt_args.append(p)
                        wt_args.append("powershell") # Perfil a usar
                        wt_args.append("-NoExit")
                        wt_args.append("-Command")
                        wt_args.append(clean_cmds)

                    # Ejecución del comando WT
                    subprocess.Popen(wt_args, shell=True)

            except Exception as e:
                print(f"Error {t}: {e}")

if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()