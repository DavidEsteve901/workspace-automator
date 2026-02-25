# Workspace Automator 🚀

Una herramienta GUI en Python para automatizar la apertura de todos los elementos de tus entornos de desarrollo (IDEs, Apps, Webs, Terminales y más) con un solo clic. Ideal para desarrolladores que quieren empezar a trabajar inmediatamente sin perder tiempo abriendo programas uno a uno.

## ✨ Características Principales

* **📁 Categorías de Trabajo:** Organiza tus entornos en diferentes perfiles (ej: "Backend", "Frontend", "Proyecto X").
* **⚡ Soporte Multi-tipo (¡Más que solo VS Code!):**
  * �‍💻 **IDEs Personalizables:** Abre carpetas de proyecto no solo con VS Code, sino con el IDE que prefieras (`code`, `cursor`, `antigravity`, `pycharm`, etc.).
  * �🖥️ **Apps (.exe):** Lanza ejecutables de escritorio tradicionales.
  * 🌐 **Web:** Abre múltiples URLs directamente en pestañas de tu navegador predeterminado.
  * � **Obsidian:** Integra y abre tus Vaults de Obsidian (`obsidian://`).
  * 💻 **Terminal Multi-Pestaña:** Abre instancias de PowerShell con múltiples pestañas y comandos iniciales preconfigurados (usando Windows Terminal `wt.exe`).
* **💾 Persistencia Automática:** Tus configuraciones y categorías se guardan automáticamente en formato JSON local (`mis_apps_config_v2.json`).
* **🎨 Interfaz Moderna y Fluida:** Construida con `CustomTkinter` para ofrecer una experiencia nativa de Modo Oscuro con temática azul elegante.
* **✍️ Editor de Comandos Integrado:** Interfaz dedicada para programar fácilmente las múltiples pestañas y comandos que quieres que se ejecuten en tus terminales al lanzar tu espacio de trabajo.

## 📦 Instalación

1. Clona este repositorio:
   ```bash
   git clone https://github.com/TU-USUARIO/workspace-automator.git
   cd workspace-automator
   ```

2. Instala las dependencias necesarias:
   ```bash
   pip install -r requirements.txt
   ```
   *(Asegúrate de tener instalada la librería `customtkinter`)*

3. **Requisito para Terminal Multi-pestaña (Opcional):**
   Para aprovechar al máximo la función de múltiples pestañas de terminal, asegúrate de tener instalado [Windows Terminal](https://apps.microsoft.com/detail/9n0dx20hk701) (ejecutable `wt.exe`), que viene por defecto en Windows 11 o se puede instalar desde la Microsoft Store en Windows 10.

## 🚀 Uso

Ejecuta el script principal desde tu terminal:

```bash
python launcher_pro.py
```

1. Crea o renombra una **Categoría** desde el menú superior.
2. Utiliza los botones inferiores para añadir las herramientas que necesitas (Apps, Webs, Proyecto IDE, Obsidian o Terminal).
3. Una vez configurado tu entorno, presiona el botón gigante **🚀 LANZAR ENTORNO**.
4. ¡Disfruta de tu setup listo en segundos!

## 🛠️ Tecnologías Utilizadas

* **Lenguaje:** Python 3.x
* **Interfaz:** [CustomTkinter](https://github.com/TomSchimansky/CustomTkinter) (por Tom Schimansky)
* **Datos:** JSON puro
* **Automatización:** Módulo nativo `subprocess` y llamadas a OS.

---

Creado por David para mejorar la productividad diaria.