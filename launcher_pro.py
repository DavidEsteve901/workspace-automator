import customtkinter as ctk
import tkinter as tk
from tkinter import filedialog, messagebox
import subprocess
import json
import os

# Configuración inicial de la apariencia
ctk.set_appearance_mode("Dark")  # Modos: "System", "Dark", "Light"
ctk.set_default_color_theme("blue")  # Temas: "blue", "green", "dark-blue"

class DevLauncherApp(ctk.CTk):
    def __init__(self):
        super().__init__()

        # Configuración de la ventana
        self.title("Dev Workspace Launcher")
        self.geometry("600x500")
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1) # El frame de apps se expande

        # Archivo de base de datos
        self.db_file = "mis_apps_config.json"
        self.apps_data = self.load_data()
        self.current_category = None

        # --- UI LAYOUT ---
        
        # 1. Cabecera y Selección de Categoría
        self.header_frame = ctk.CTkFrame(self)
        self.header_frame.grid(row=0, column=0, padx=20, pady=(20, 10), sticky="ew")
        
        self.lbl_title = ctk.CTkLabel(self.header_frame, text="Modo de Trabajo:", font=("Roboto", 16, "bold"))
        self.lbl_title.pack(side="left", padx=10)

        self.category_option = ctk.CTkOptionMenu(self.header_frame, values=[], command=self.change_category)
        self.category_option.pack(side="left", padx=10)

        self.btn_add_cat = ctk.CTkButton(self.header_frame, text="+ Nueva Categoría", width=100, command=self.add_category_dialog)
        self.btn_add_cat.pack(side="right", padx=10)

        # 2. Botón de Acción Principal (LANZAR TODO)
        self.btn_launch_all = ctk.CTkButton(self, text="🚀 LANZAR ENTORNO", height=50, font=("Roboto", 18, "bold"), fg_color="#2CC985", hover_color="#24A36B", command=self.launch_workspace)
        self.btn_launch_all.grid(row=1, column=0, padx=20, pady=10, sticky="ew")

        # 3. Lista de Aplicaciones (Scrollable)
        self.apps_frame = ctk.CTkScrollableFrame(self, label_text="Aplicaciones en esta categoría")
        self.apps_frame.grid(row=2, column=0, padx=20, pady=10, sticky="nsew")

        # 4. Footer (Añadir App)
        self.btn_add_app = ctk.CTkButton(self, text="Añadir Aplicación (.exe)", command=self.add_app_to_list)
        self.btn_add_app.grid(row=3, column=0, padx=20, pady=20, sticky="ew")

        # Inicializar datos
        self.refresh_categories()

    def load_data(self):
        """Carga el JSON o crea uno vacío si no existe."""
        if os.path.exists(self.db_file):
            try:
                with open(self.db_file, "r") as f:
                    return json.load(f)
            except:
                return {}
        return {}

    def save_data(self):
        """Guarda los cambios en el JSON."""
        with open(self.db_file, "w") as f:
            json.dump(self.apps_data, f, indent=4)

    def refresh_categories(self):
        """Actualiza el dropdown de categorías."""
        categories = list(self.apps_data.keys())
        if not categories:
            categories = ["General"]
            self.apps_data["General"] = []
            self.save_data()
        
        self.category_option.configure(values=categories)
        
        # Seleccionar la primera por defecto si no hay seleccionada
        if not self.current_category or self.current_category not in categories:
            self.current_category = categories[0]
            self.category_option.set(categories[0])
        
        self.refresh_apps_list()

    def change_category(self, choice):
        self.current_category = choice
        self.refresh_apps_list()

    def refresh_apps_list(self):
        """Dibuja la lista de apps de la categoría actual."""
        # Limpiar frame
        for widget in self.apps_frame.winfo_children():
            widget.destroy()

        apps = self.apps_data.get(self.current_category, [])

        if not apps:
            lbl = ctk.CTkLabel(self.apps_frame, text="No hay apps configuradas aún.", text_color="gray")
            lbl.pack(pady=20)
            return

        for app_path in apps:
            row = ctk.CTkFrame(self.apps_frame)
            row.pack(fill="x", pady=5, padx=5)

            # Nombre del archivo (solo el nombre, no la ruta completa)
            app_name = os.path.basename(app_path)
            lbl_name = ctk.CTkLabel(row, text=app_name, anchor="w")
            lbl_name.pack(side="left", padx=10, fill="x", expand=True)

            # Botón borrar
            btn_del = ctk.CTkButton(row, text="X", width=30, fg_color="#FF5555", hover_color="#AA0000",
                                    command=lambda p=app_path: self.remove_app(p))
            btn_del.pack(side="right", padx=5, pady=5)

    def add_category_dialog(self):
        dialog = ctk.CTkInputDialog(text="Nombre de la nueva categoría:", title="Nueva Categoría")
        new_cat = dialog.get_input()
        if new_cat:
            if new_cat not in self.apps_data:
                self.apps_data[new_cat] = []
                self.save_data()
                self.current_category = new_cat
                self.refresh_categories()
                self.category_option.set(new_cat)

    def add_app_to_list(self):
        file_path = filedialog.askopenfilename(title="Selecciona el ejecutable", filetypes=[("Ejecutables", "*.exe"), ("Todos", "*.*")])
        if file_path:
            # Normalizar ruta para evitar problemas con barras invertidas
            file_path = os.path.normpath(file_path)
            
            if file_path not in self.apps_data[self.current_category]:
                self.apps_data[self.current_category].append(file_path)
                self.save_data()
                self.refresh_apps_list()

    def remove_app(self, app_path):
        if app_path in self.apps_data[self.current_category]:
            self.apps_data[self.current_category].remove(app_path)
            self.save_data()
            self.refresh_apps_list()

    def launch_workspace(self):
        apps = self.apps_data.get(self.current_category, [])
        if not apps:
            messagebox.showwarning("Atención", "No hay aplicaciones en esta lista para ejecutar.")
            return
        
        count = 0
        for app_path in apps:
            try:
                # subprocess.Popen abre la app y no bloquea el script
                subprocess.Popen(app_path)
                count += 1
            except Exception as e:
                print(f"Error al abrir {app_path}: {e}")
                messagebox.showerror("Error", f"No se pudo abrir: {os.path.basename(app_path)}\n\nVerifica si la ruta cambió.")
        
        # Opcional: Cerrar el launcher después de lanzar
        # self.destroy() 

if __name__ == "__main__":
    app = DevLauncherApp()
    app.mainloop()