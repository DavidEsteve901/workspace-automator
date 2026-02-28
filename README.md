# Workspace Automator 🚀

Una herramienta GUI profesional en Python para automatizar la apertura de tus entornos de desarrollo (IDEs, Apps, Webs, Terminales) con integración profunda en **Windows 11**, **PowerToys FancyZones** y **Escritorios Virtuales**.

## ✨ Características Principales

*   **📁 Gestión de Categorías:** Organiza tus flujos de trabajo (ej: "Backend Go", "Frontend React", "Data Science").
*   **🖥️ Soporte Nativo de Monitores y Escritorios:** Elige en qué monitor físico y en qué escritorio virtual debe aparecer cada aplicación. El launcher se encarga de cambiar de escritorio automáticamente antes de lanzar.
*   **🧩 Integración Inteligente con FancyZones:**
    *   **Auto-Ajuste Dinámico:** Si mueves una ventana manualmente a otra zona usando `Shift + Drag` de FancyZones, el script lo detecta automáticamente y la une al grupo de esa zona para que puedas seguir rotando entre ellas.
    *   **Fuzzy Matching de Zonas:** El sistema identifica las zonas por posición física (Monitor + Índice de Zona), siendo inmune a cambios accidentales de Layout o UUIDs.
*   **📚 Apilamiento (Stacking) y Rotación:** Soporte total para la rotación de ventanas en la misma zona (atajo `Win + Alt + Izquierda/Derecha`). Ideal para tener varias terminales o navegadores en el mismo hueco y saltar entre ellos con un solo clic.
*   **🛡️ Motores de Integridad y Foco:**
    *   **Repaso Final:** Tras lanzar todo el entorno, el script realiza un barrido final de seguridad (1.5s después) para recolocar cualquier ventana que se haya movido accidentalmente durante la carga.
    *   **Foco Forzado:** Técnicas avanzadas (`SwitchToThisWindow` + Simulación Alt) para garantizar que las ventanas carguen en primer plano y listas para usar.
*   **👨‍💻 Terminales Inteligentes:** Soporte para múltiples instancias de Windows Terminal que se registran independientemente en sus zonas.
*   **💾 Persistencia JSON:** Todas tus configuraciones se guardan localmente en `mis_apps_config_v2.json`.

## 📦 Instalación y Requisitos

1.  **Clonar y entrar al directorio:**
    ```bash
    git clone https://github.com/TU-USUARIO/workspace-automator.git
    cd workspace-automator
    ```

2.  **Instalar dependencias de Python:**
    ```bash
    python -m pip install -r requirements.txt
    ```
    *(Incluye `customtkinter`, `pyvda`, `pywin32`, `pynput` y librerías nativas de Windows)*

3.  **⚠️ Configuración de PowerToys ⚠️**
    *   Abre **PowerToys Settings** > **FancyZones**.
    *   En **Comportamiento de la ventana**, activa: **`Mover las ventanas recién creadas a su última zona conocida`**.
    *   Esto permite que FancyZones ayude con el posicionamiento inicial, mientras el Launcher gestiona la lógica de grupos en tiempo real.

## 🚀 Uso

Lanza la interfaz principal:
```bash
python launcher_pro.py
```

1.  **Configurar FancyZones:** Pulsa el botón "Configurar FancyZones" para detectar tus layouts y monitores.
2.  **Añadir Elementos:** Configura Apps, URLs o Proyectos. Selecciona el Monitor, Escritorio y la Zona específica.
3.  **Lanzar:** Pulsa **🚀 LANZAR ENTORNO**. Puedes seguir usando el PC; el sistema hará un repaso final para asegurar que todo quede perfecto.
4.  **Gestionar en vivo:** Mueve ventanas entre zonas con `Shift + Drag`. El script se actualizará solo. Usa `Win + Alt + Flechas` para rotar ventanas dentro de una misma zona.

---

### 🛠️ Detalles Técnicos

El script utiliza un **Hybrid Runtime Engine**:
-   **Static Launch**: Inyecta configuraciones en FancyZones y usa posicionamiento matemático directo para el despliegue inicial.
-   **Dynamic Tracking**: Un listener de ratón global detecta los encajes de FancyZones en tiempo real, actualizando los stacks internos de ventanas mediante un sistema de "Fuzzy Key Matching" (Monitor + ZoneID).
-   **Virtual Desktop API**: Integración con `pyvda` para saltos limpios entre escritorios virtuales de Windows 11.

Creado para llevar la productividad en Windows al siguiente nivel. 💻✨
