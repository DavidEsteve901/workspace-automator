# 🛡️ Estrategia de Distribución Segura - Workspace Launcher

Este documento detalla el sistema de compilación y distribución diseñado para proteger la propiedad intelectual y facilitar el despliegue a testers y clientes finales.

## 🏗️ Filosofía de "Binario Sellado"

Para garantizar que el código fuente (C#) y los activos de la interfaz (React/Vite) no sean visibles ni manipulables por el usuario final, hemos implementado las siguientes medidas:

1.  **Incrustación de Recursos (Embedded Resources):** Todo el contenido de la carpeta `frontend/dist` se compila directamente dentro del ensamblado de C#. No se generan carpetas `wwwroot` ni archivos `.js`/`.html` externos.
2.  **Servidor de Recursos Interno:** La aplicación utiliza un manejador de recursos en memoria para servir la interfaz a través de `https://launcher.local/` sin tocar el disco duro.
3.  **Ejecutable Autónomo (Self-Contained):** El `.exe` generado incluye todas las librerías de .NET necesarias, permitiendo que funcione en cualquier PC con Windows 10/11 sin instalar nada previamente.

---

## 🚀 Compilación Local (Para el Desarrollador)

Si deseas generar el ejecutable manualmente en tu máquina:

1.  Asegúrate de tener instalados **Node.js 20+** y **.NET 8 SDK**.
2.  Ejecuta el script de automatización:
    ```powershell
    .\build.ps1 -Publish
    ```
3.  El resultado estará en `publish/WorkspaceLauncher.exe`. **Este es el único archivo que necesitas distribuir.** Se han eliminado archivos `.pdb`, `.xml` y otros elementos innecesarios para una apariencia 100% profesional.

---

## 🧼 Experiencia de Usuario "Limpia"

Hemos configurado la aplicación para que sea lo más discreta posible:
- **Sin carpetas de caché visibles:** La carpeta de datos de WebView2 (`.WebView2`) ahora se guarda en `%LocalAppData%\WorkspaceLauncher\WebView2Cache`, evitando "ensuciar" el escritorio o la carpeta de descarga del usuario.
- **Sin archivos de depuración:** La compilación de publicación (`-Publish`) no genera archivos `.pdb` ni documentación XML de dependencias.
- **Resiliencia en AppData:** Si el usuario mueve el `.exe` a `C:\Program Files`, la aplicación seguirá funcionando correctamente ya que escribe sus datos temporales y de configuración en sus carpetas de usuario correspondientes.

---

## 🤖 Automatización con GitHub Actions

Hemos configurado un flujo de trabajo profesional en `.github/workflows/release.yml` que se activa automáticamente.

### 1. Generación de Versiones (Releases)
Cada vez que crees una "Etiqueta" (Tag) en Git, GitHub compilará el proyecto desde cero y creará una descarga oficial:
- **Paso 1:** Crea el tag y súbelo: `git tag v1.0.0-beta && git push origin v1.0.0-beta`
- **Paso 2:** Ve a la pestaña **Actions** en GitHub.
- **Paso 3:** Una vez finalizado, el archivo `WorkspaceLauncher.zip` estará disponible en la sección de **Releases** de tu repositorio privado.

### 2. Descarga Directa para Testers (Artifacts)
Si quieres pasarle el ejecutable a un amigo sin invitarle a GitHub:
1.  Ve a la última ejecución exitosa en **Actions**.
2.  Al final de la página, encontrarás una sección llamada **Artifacts**.
3.  Descarga el `WorkspaceLauncher-Binary`, súbelo a Google Drive o pásaselo directamente.

---

## ⚙️ Configuración Dinámica Cero

Para evitar errores en el PC de los testers, el sistema de configuración (`ConfigManager.cs`) se ha rediseñado:

- **Búsqueda Inteligente:** Primero busca el archivo `mis_apps_config_v2.json` junto al ejecutable (ideal para portabilidad).
- **Fallback en AppData:** Si no lo encuentra, crea automáticamente una carpeta en `%AppData%\WorkspaceLauncher` y genera una configuración por defecto.
- **Resiliencia:** La aplicación nunca "explota" si falta el archivo de configuración; siempre garantiza un estado inicial válido.

---

## 🔒 Recomendaciones de Seguridad

1.  **Repositorio Privado:** Mantén siempre el repositorio en modo "Privado" en GitHub Settings.
2.  **Ofuscación (Opcional):** Para una protección extra de nivel bancario, podrías pasar el `.exe` final por un ofuscador de .NET (como *Obfuscar* o *ConfuserEx*), aunque la compilación *Single-File* ya añade una capa importante de dificultad para la ingeniería inversa.

---

> [!IMPORTANT]
> **No compartas nunca el código fuente**. Distribuye únicamente el archivo `.zip` generado por los Artifacts de GitHub o la sección de Releases.
