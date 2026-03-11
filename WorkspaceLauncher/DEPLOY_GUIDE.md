# 🚀 Guía de Compilación y Publicación - Workspace Launcher

Esta guía explica detalladamente cómo generar el archivo ejecutable (`.exe`) de la aplicación y cómo publicarlo correctamente en GitHub para su distribución.

## 1. Compilación (Generar el .exe)

La aplicación está configurada para compilarse en un **único archivo ejecutable (Single File)** que incluye tanto el backend (C#) como el frontend (React).

### Pasos

1. Abre una terminal (PowerShell) en la carpeta `WorkspaceLauncher`.

2. Ejecuta el script de compilación con el flag de publicación:

   ```powershell
   .\build.ps1 -Publish
   ```

3. El script realizará las siguientes tareas:
   - Compilará el frontend de React (`npm run build`).
   - Compilará el proyecto C# en modo *Release*.
   - Empaquetará todo en un archivo único optimizado para Windows x64.

### Resultado

El archivo generado se encontrará en:
`WorkspaceLauncher\publish\WorkspaceLauncher.exe`

---

## 2. Preparación para la Distribución

Aunque el `.exe` es portable, para que la aplicación funcione con tus datos existentes (Workspaces), se recomienda distribuirlo de la siguiente manera:

1. Crea una carpeta nueva (ej: `WorkspaceLauncher_v1.0.0`).
2. Copia el `WorkspaceLauncher.exe` dentro.
3. Copia tu archivo `mis_apps_config_v2.json` dentro de esa misma carpeta.
4. **Comprime la carpeta en un archivo .ZIP**. Esto es lo que subirás a GitHub.

---

## 3. Publicación en GitHub (Releases)

Para que tu proyecto tenga un aspecto profesional y sea fácil de descargar:

1. Ve a tu repositorio en GitHub.
2. En la barra lateral derecha, haz clic en **"Releases"** -> **"Create a new release"**.
3. **Tag version**: Pon un número de versión, por ejemplo `v1.0.0`.
4. **Release title**: Un nombre descriptivo, ej: `Workspace Launcher v1.0.0 - Stable`.
5. **Description**: Describe los cambios o nuevas funcionalidades.
6. **Binaries (Assets)**: Arrastra y suelta el archivo **.ZIP** que creaste en el punto anterior.
7. Haz clic en **"Publish release"**.

---

## 4. Cómo instalar/usar (Instrucciones para el usuario)

Cuando alguien (o tú mismo en otro PC) descargue la app:

1. Descargar el `.ZIP` desde la sección de *Releases*.

2. Extraer el contenido en cualquier carpeta (ej: `C:\Herramientas\Launcher`).

3. Ejecutar `WorkspaceLauncher.exe`.
   - *Nota: La app se iniciará y se minimizará automáticamente al área de notificación (System Tray).*

4. Para abrir la interfaz, haz clic en el icono de la app junto al reloj de Windows.

---

### Notas Pro

- **DPI / Resolución**: La app está optimizada para monitores de alta resolución.
- **Portabilidad**: No escribe nada en el registro de Windows, todo se guarda en el archivo JSON local.
