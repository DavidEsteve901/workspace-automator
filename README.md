# Workspace Automator 🚀

Una herramienta GUI profesional en Python para automatizar la apertura de tus entornos de desarrollo (IDEs, Apps, Webs, Terminales) con integración profunda en **Windows 11**, **PowerToys FancyZones** y **Escritorios Virtuales**.

## ✨ Características Principales

* **📁 Gestión de Categorías:** Organiza tus flujos de trabajo (ej: "Backend Go", "Frontend React", "Data Science").
* **🖥️ Soporte Nativo de Monitores y Escritorios:** Elige en qué monitor físico y en qué escritorio virtual debe aparecer cada aplicación. El launcher se encarga de cambiar de escritorio automáticamente antes de lanzar.
* **🔮 Integración "Hacker" con FancyZones:**  
  A diferencia de otros scripts, esta herramienta **no mueve ventanas manualmente**. En su lugar, realiza una **inyección de historial** directamente en los archivos de configuración de PowerToys (`app-zone-history.json`) antes del lanzamiento.
* **📚 Apilamiento (Stacking) de Ventanas:** Soporte total para la rotación de ventanas en la misma zona de FancyZones (atajo `Win + RePág / AvPág`). Ideal para tener varias terminales o navegadores en el mismo hueco y saltar entre ellos.
* **👨‍💻 Terminales Inteligentes:** Configura múltiples instancias de terminal que se abren como ventanas independientes, permitiendo que cada una se registre en su zona de FancyZones correspondiente.
* **💾 Persistencia JSON:** Todas tus configuraciones se guardan localmente en `mis_apps_config_v2.json` (auto-ignorado en git).

## 📦 Instalación y Requisitos

1. **Clonar y entrar al directorio:**

   ```bash
   git clone https://github.com/TU-USUARIO/workspace-automator.git
   cd workspace-automator
   ```

2. **Instalar dependencias de Python:**

   ```bash
   python -m pip install -r requirements.txt
   ```

   *(Incluye `customtkinter`, `pyvda`, `pywin32`, `pygetwindow`, `pyautogui` para la magia Win11)*

3. **⚠️ Configuración CRÍTICA de PowerToys ⚠️**

   > [!IMPORTANT]
   > Para que el Launcher pueda comunicarse con PowerToys y posicionar las ventanas correctamente en sus zonas (sin usar falsos movimientos de ratón), tienes que activar **obligatoriamente** esta opción en tu Windows:

   * Abre los ajustes de tu PC: **PowerToys Settings** > **FancyZones**.
   * Baja a la sección **Comportamiento de la ventana** (Window behavior).
   * Activa la palanca que dice: **`Mover las ventanas recién creadas a su última zona conocida`** (Move newly created windows to their last known zone).
   * Asegúrate de que, justo debajo, el rastreo de ventana esté basado en "Ruta de aplicación o Título".
   * *(Opcional pero Recomendado)*: En la sección **Cambiar entre ventanas de la zona**, elige tus atajos (ej: `Win + RePág/AvPág`). Esto te permitirá saltar instantáneamente entre todas las pestañas de terminales o webs que caigan apiladas en el mismo recuadro de zona.

4. **Windows Terminal:**

   Asegúrate de tener instalado [Windows Terminal](https://apps.microsoft.com/detail/9n0dx20hk701) (`wt.exe`) para el soporte de terminales avanzado.

## 🚀 Uso

Lanza la interfaz principal:

```bash
python launcher_pro.py
```

1. **Configurar FancyZones:** Pulsa el botón "Configurar FancyZones" para detectar tus layouts activos.
2. **Añadir Elementos:** Usa los botones inferiores para añadir Apps, URLs o Carpetas de Proyecto.
3. **Editor Avanzado:** Al añadir o editar, selecciona el Monitor, el Escritorio y la Zona específica del layout donde quieres que "caiga" la aplicación.
4. **Lanzar:** Pulsa el botón gigante **🚀 LANZAR ENTORNO** y observa cómo todo tu espacio de trabajo se monta solo en segundos.

---

### 🛠️ Detalles Técnicos

El script utiliza `pyvda` para la manipulación de la API de Escritorios Virtuales de Windows y accede directamente a `%LOCALAPPDATA%\Microsoft\PowerToys\FancyZones` para leer y escribir la configuración en tiempo real, garantizando una integración nativa sin parpadeos ni movimientos de ratón fantasmales.

Creado para llevar la productividad en Windows al siguiente nivel. 💻✨