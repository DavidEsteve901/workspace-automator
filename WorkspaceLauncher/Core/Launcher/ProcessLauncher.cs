using System.Diagnostics;
using System.IO;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Handles launching individual items by type.
/// Port of the Python launch logic per item type (Phase 2).
///
/// Rules from the Python script:
///  - URL: ALWAYS --new-window, then additional URLs as args in same call
///  - PowerShell: use wt.exe if available, else fallback to powershell.exe
///  - VsCode/IDE: shell command with the path
///  - Obsidian: URI scheme obsidian://open?vault=...
///  - EXE: Process.Start with UseShellExecute
/// </summary>
public static class ProcessLauncher
{
    private const string TabSeparator = "--- NUEVA PESTAÑA ---";

    public static Process? Launch(AppItem item)
    {
        try
        {
            return item.Type switch
            {
                "exe"        => LaunchExe(item),
                "url"        => LaunchUrl(item),
                "ide"        => LaunchIde(item),
                "vscode"     => LaunchVsCode(item),
                "powershell" => LaunchPowerShell(item),
                "obsidian"   => LaunchObsidian(item),
                _            => LaunchExe(item)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessLauncher] Error launching {item.Type} '{item.Path}': {ex.Message}");
            return null;
        }
    }

    // ── Launchers ─────────────────────────────────────────────────────────────

    private static Process? LaunchExe(AppItem item)
    {
        if (!File.Exists(item.Path))
        {
            // Try as shell-executable (e.g. path-in-PATH programs)
            Console.WriteLine($"[ProcessLauncher] EXE not found at path, trying shell: {item.Path}");
        }
        return Process.Start(new ProcessStartInfo
        {
            FileName         = item.Path,
            UseShellExecute  = true,
            WorkingDirectory = File.Exists(item.Path)
                ? (Path.GetDirectoryName(item.Path) ?? "")
                : "",
        });
    }

    private static Process? LaunchUrl(AppItem item)
    {
        // Collect all URLs: primary from Path, additional from Cmd
        var allEntries = new List<string>();
        if (!string.IsNullOrEmpty(item.Path))
            allEntries.Add(item.Path.Trim());

        if (!string.IsNullOrEmpty(item.Cmd))
        {
            var extra = item.Cmd
                .Split(TabSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            allEntries.AddRange(extra);
        }

        if (allEntries.Count == 0) return null;

        string browserExe = ResolveBrowser(item.Browser);

        if (string.IsNullOrEmpty(browserExe))
        {
            // Default browser via shell — can't force --new-window but respect the rule as best we can
            // Open first URL with shell, rest also with shell (each may get its own window depending on browser)
            foreach (var url in allEntries)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }

        // RULE: ALWAYS --new-window for the first URL so it NEVER reuses an existing browser window.
        // All additional URLs go as extra args so they open as TABS inside that new window.
        string args = $"--new-window \"{allEntries[0]}\"";
        if (allEntries.Count > 1)
            args += " " + string.Join(" ", allEntries.Skip(1).Select(u => $"\"{u}\""));

        Console.WriteLine($"[ProcessLauncher] Browser launch: {browserExe} {args}");
        return Process.Start(new ProcessStartInfo
        {
            FileName        = browserExe,
            Arguments       = args,
            UseShellExecute = false,
        });
    }

    private static Process? LaunchVsCode(AppItem item)
    {
        // VS Code is launched via shell so it resolves 'code' from PATH
        return Process.Start(new ProcessStartInfo
        {
            FileName         = "code",
            Arguments        = $"\"{item.Path}\"",
            UseShellExecute  = true,
            WorkingDirectory = item.Path,
        });
    }

    private static Process? LaunchIde(AppItem item)
    {
        string? ideCmd = item.IdeCmd;
        if (string.IsNullOrEmpty(ideCmd)) return null;

        return Process.Start(new ProcessStartInfo
        {
            FileName         = ideCmd,
            Arguments        = $"\"{item.Path}\"",
            UseShellExecute  = true,
            WorkingDirectory = item.Path,
        });
    }

    private static Process? LaunchPowerShell(AppItem item)
    {
        var tabs = (item.Cmd ?? "")
            .Split(TabSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Build a single string for each tab: the path + optional command
        string workDir = item.Path ?? "";

        // Try Windows Terminal first (preferred)
        if (IsWindowsTerminalAvailable())
        {
            string args = BuildWtArgs(workDir, tabs);
            Console.WriteLine($"[ProcessLauncher] wt.exe launch: {args}");
            return Process.Start(new ProcessStartInfo
            {
                FileName         = "wt.exe",
                Arguments        = args,
                UseShellExecute  = false,
            });
        }

        // Fallback: standard powershell.exe / pwsh.exe
        string psExe = GetAvailablePowerShell();
        Console.WriteLine($"[ProcessLauncher] wt.exe not found, falling back to {psExe}");
        
        string cmd = tabs.Length > 0 ? EscapeCmd(tabs[0]) : "";
        string psArgs = string.IsNullOrEmpty(cmd)
            ? ""
            : $"-NoExit -Command \"{cmd}\"";

        return Process.Start(new ProcessStartInfo
        {
            FileName         = psExe,
            Arguments        = psArgs,
            UseShellExecute  = false,
            WorkingDirectory = workDir,
        });
    }

    private static string GetAvailablePowerShell()
    {
        // Check for pwsh (PowerShell 7+) first
        string? pwsh = FindExe("pwsh.exe");
        if (pwsh != null && File.Exists(pwsh)) return "pwsh.exe";
        
        // Final fallback to Windows PowerShell
        return "powershell.exe";
    }

    private static string BuildWtArgs(string workDir, string[] tabs)
    {
        // wt.exe syntax: wt -w -1 -d "<dir>" pwsh -NoExit [-Command "..."] ; new-tab -d "<dir>" pwsh ...
        // -w -1 means "open in a new window" (not the last used one)
        var parts = new List<string>();
        parts.Add("-w -1");

        string psExeForWt = GetAvailablePowerShell();

        for (int i = 0; i < Math.Max(1, tabs.Length); i++)
        {
            string tabCmd = tabs.Length > i ? tabs[i] : "";
            string psInner = string.IsNullOrEmpty(tabCmd)
                ? $"-d \"{workDir}\" {psExeForWt} -NoExit"
                : $"-d \"{workDir}\" {psExeForWt} -NoExit -Command \"{EscapeCmd(tabCmd)}\"";

            if (i == 0)
                parts.Add(psInner);
            else
                parts.Add($"; new-tab {psInner}");
        }

        return string.Join(" ", parts);
    }

    private static Process? LaunchObsidian(AppItem item)
    {
        // Obsidian uses URI scheme: obsidian://open?vault=<vaultName>
        // Optionally: obsidian://open?path=<fullPath> — but vault name is more reliable
        string vaultName = Path.GetFileName(item.Path.TrimEnd('\\', '/'));
        string uri       = $"obsidian://open?vault={Uri.EscapeDataString(vaultName)}";
        Console.WriteLine($"[ProcessLauncher] Obsidian URI: {uri}");
        return Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsWindowsTerminalAvailable()
    {
        return FindExe("wt.exe") != null;
    }

    private static string? FindExe(string exeName)
    {
        // Check common install paths
        var candidates = new[]
        {
            // wt.exe is a WindowsApps exe, accessible via PATH on most Win11 systems
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps", exeName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"PowerShell\7", exeName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"PowerShell\7", exeName),
        };

        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        // Try PATH
        return Environment.GetEnvironmentVariable("PATH")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p, exeName))
            .FirstOrDefault(File.Exists);
    }

    private static string ResolveBrowser(string? browser)
    {
        if (string.IsNullOrEmpty(browser) || browser == "default") return string.Empty;

        return browser switch
        {
            "msedge"  => FindBrowserPath("msedge.exe",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
            "chrome"  => FindBrowserPath("chrome.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            "firefox" => FindBrowserPath("firefox.exe",
                @"C:\Program Files\Mozilla Firefox\firefox.exe"),
            "brave"   => FindBrowserPath("brave.exe",
                @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"),
            _         => browser // Custom path passed directly
        };
    }

    private static string FindBrowserPath(string exeName, string defaultPath)
    {
        if (File.Exists(defaultPath)) return defaultPath;
        return Environment.GetEnvironmentVariable("PATH")
            ?.Split(';')
            .Select(p => Path.Combine(p, exeName))
            .FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static string EscapeCmd(string cmd) => cmd.Replace("\"", "\\\"");
}
