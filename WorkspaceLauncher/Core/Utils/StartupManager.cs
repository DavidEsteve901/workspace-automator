using System.Diagnostics;
using Microsoft.Win32;
using WorkspaceLauncher.Core.Utils;

namespace WorkspaceLauncher.Core.Utils;

/// <summary>
/// Manages Windows Registry for "Run at Startup" functionality.
/// </summary>
public static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WorkspaceLauncher";

    /// <summary>
    /// Gets the current executable path. Using Process.GetCurrentProcess().MainModule.FileName
    /// ensures that if the app is moved (portable mode), the registry path can be updated.
    /// </summary>
    private static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    /// <summary>
    /// Sets whether the application should run at Windows startup.
    /// </summary>
    public static void SetRunAtStartup(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null)
            {
                Logger.Error($"[StartupManager] Could not open registry key: {RegistryPath}");
                return;
            }

            if (enabled)
            {
                string path = GetExecutablePath();
                key.SetValue(AppName, $"\"{path}\"");
                Logger.Info($"[StartupManager] Enabled run at startup. Path: {path}");
            }
            else
            {
                key.DeleteValue(AppName, false);
                Logger.Info("[StartupManager] Disabled run at startup.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[StartupManager] Error updating registry: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronizes the registry state with the provided setting.
    /// Also updates the path if the executable has moved.
    /// </summary>
    public static void Sync(bool shouldBeEnabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            string? currentVal = key?.GetValue(AppName) as string;
            string expectedVal = $"\"{GetExecutablePath()}\"";

            bool isCurrentlyEnabled = currentVal != null;

            if (shouldBeEnabled)
            {
                // Enable or update path if moved
                if (!isCurrentlyEnabled || currentVal != expectedVal)
                {
                    SetRunAtStartup(true);
                }
            }
            else
            {
                // Disable if it's currently enabled
                if (isCurrentlyEnabled)
                {
                    SetRunAtStartup(false);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[StartupManager] Sync error: {ex.Message}");
        }
    }
}
