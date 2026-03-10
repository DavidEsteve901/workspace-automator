# Workspace Automator Pro 🚀

**Workspace Automator Pro** es la evolución definitiva de la gestión de entornos de trabajo en Windows. Diseñada sobre una arquitectura robusta de **.NET 8 (WPF)** y una interfaz moderna en **React**, esta herramienta permite automatizar la apertura y posicionamiento quirúrgico de tus aplicaciones, navegadores y terminales, integrándose profundamente con **PowerToys FancyZones** y **Escritorios Virtuales**.

![Aesthetic UI](https://via.placeholder.com/800x450/121212/FFFFFF?text=Workspace+Automator+Pro+UI) <!-- Sustituir por imagen real si está disponible -->

## ✨ Características Estelares

*   **⚡ Motor de Convergencia**: Lanza decenas de aplicaciones de forma optimizada (secuencial por escritorio, paralelo por categoría) asegurando que ninguna ventana se "pierda" durante la carga.
*   **🌐 Portabilidad Total (Cloud Sync Friendly)**: Gestiona tus archivos de configuración (`.json`) desde cualquier lugar. Cambia la ubicación de tus datos a **Google Drive** o **OneDrive** directamente desde la interfaz.
*   **🧩 Sincronización Inteligente de FancyZones**: ¿Trabajas en varios PCs? La app detecta si falta un layout en el equipo actual y lo **inyecta automáticamente** desde su caché interno.
*   **🛡️ Auditoría de Integridad (Double Pass)**: Un sistema de vigilancia post-lanzamiento que verifica dos veces la posición y el escritorio de cada ventana, corrigiendo cualquier desviación de forma invisible.
*   **🖼️ Interfaz Premium**: Experiencia de usuario de última generación construida con React, Lucide Icons y CSS moderno, integrada mediante WebView2 para máxima fluidez.
*   **🖥️ Control Multinivel**: 
    *   Soporte total para **Monitor-Aware snapping**.
    *   Cambio automático de **Escritorios Virtuales de Windows 11**.
    *   Detección inteligente de ventanas mediante Scoring (Título, Proceso, Ruta).

---

## 🛠️ Instalación y Compilación

Este proyecto consta de un backend en C# y un frontend en React.

### Requisitos
*   [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   [Node.js / npm](https://nodejs.org/) (para compilar la UI)
*   [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (incluido en Windows 11)

### Pasos para Compilar

1.  **Preparar el Frontend**:
    ```powershell
    cd WorkspaceLauncher/frontend
    npm install
    npm run build
    ```
    *Esto generará los archivos estáticos en `WorkspaceLauncher/frontend/dist`.*

2.  **Compilar el Ejecutable (Backend)**:
    Regresa a la raíz de la carpeta `WorkspaceLauncher` y ejecuta:
    ```powershell
    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
    ```
    *Encontrarás el ejecutable listo para usar en `WorkspaceLauncher/bin/Release/net8.0-windows/win-x64/publish/`.*

---

## 🚀 Cómo empezar

1.  **Define tu Ubicación**: Por defecto, la app guarda todo en la carpeta del ejecutable. Si quieres sincronizar entre PCs, ve a **Configuración** y cambia la ruta a una carpeta en tu nube (Drive/OneDrive).
2.  **Crea un Workspace**: Añade una categoría (ej: "Productividad") y empieza a añadir aplicaciones.
3.  **Captura Layouts**: Asegúrate de tener PowerToys FancyZones activo. La app detectará automáticamente tus zonas y monitores.
4.  **Lanza y Disfruta**: Pulsa el botón principal y observa cómo tu PC se organiza solo por arte de magia.

---

## 📂 Estructura del Proyecto

*   `/WorkspaceLauncher`: Proyecto principal de C# / WPF.
    *   `/Core`: Lógica de orquestación, interop nativo y gestión de zonas.
    *   `/Bridge`: El puente de comunicación JSON entre C# y la UI.
    *   `/frontend`: Código fuente de la interfaz en React (Vite).
*   `mis_apps_config_v2.json`: Tu archivo de configuración portátil (generado al iniciar).

---

## 👨‍💻 Contribución y Repositorio

Para subir este proyecto a **GitHub**:

1.  Crea un nuevo repositorio en tu cuenta.
2.  Empuja el código:
    ```powershell
    git init
    git add .
    git commit -m "Initial commit of Workspace Automator Pro (.NET/React version)"
    git branch -M main
    git remote add origin https://github.com/TU-USUARIO/VALLE-REPLACE-THIS.git
    git push -u origin main
    ```
3.  **Releases**: Una vez compilado, puedes subir el archivo `.exe` generado en el paso de "dotnet publish" a la sección de **Releases** de GitHub para que otros puedan descargarlo e instalarlo directamente sin compilar.

---
*Desarrollado con ❤️ para maximizar el flujo de trabajo en Windows.*
