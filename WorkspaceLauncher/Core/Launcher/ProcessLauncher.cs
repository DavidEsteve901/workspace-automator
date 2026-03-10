using System.Diagnostics;
using WorkspaceLauncher.Core.Config;

namespace WorkspaceLauncher.Core.Launcher;

/// <summary>
/// Handles launching individual items by type.
/// Port of the Python launch logic per item type.
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
                "exe"       => LaunchExe(item),
                "url"       => LaunchUrl(item),
                "ide"       => LaunchIde(item),
                "vscode"    => LaunchVsCode(item),
                "powershell"=> LaunchPowerShell(item),
                "obsidian"  => LaunchObsidian(item),
                _           => LaunchExe(item)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessLauncher] Error launching {item.Type} '{item.Path}': {ex.Message}");
            return null;
        }
    }

    private static Process? LaunchExe(AppItem item)
    {
        if (!File.Exists(item.Path))
        {
            Console.WriteLine($"[ProcessLauncher] EXE not found: {item.Path}");
            return null;
        }
        return Process.Start(new ProcessStartInfo
        {
            FileName         = item.Path,
            UseShellExecute  = true,
            WorkingDirectory = Path.GetDirectoryName(item.Path) ?? "",
        });
    }

    private static Process? LaunchUrl(AppItem item)
    {
        var urls = (item.Cmd ?? item.Path)
            .Split(TabSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        string browserExe = ResolveBrowser(item.Browser);
        if (string.IsNullOrEmpty(browserExe))
        {
            // Use default browser via shell
            foreach (var url in urls)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }

        // Open first URL in a new window, rest as --new-tab
        string args = $"--new-window {urls[0]}";
        if (urls.Length > 1)
            args += " " + string.Join(" ", urls.Skip(1));

        return Process.Start(new ProcessStartInfo
        {
            FileName        = browserExe,
            Arguments       = args,
            UseShellExecute = false,
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

    private static Process? LaunchVsCode(AppItem item)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName         = "code",
            Arguments        = $"\"{item.Path}\"",
            UseShellExecute  = true,
            WorkingDirectory = item.Path,
        });
    }

    private static Process? LaunchPowerShell(AppItem item)
    {
        // Build Windows Terminal tabs
        // cmd: "cmd1 --- NUEVA PESTAÑA --- cmd2" → wt.exe new-tab -p "..." ; new-tab ...
        var cmds = (item.Cmd ?? "")
            .Split(TabSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (cmds.Length == 0) return null;

        string firstTab  = $"-d \"{item.Path}\" pwsh -NoExit -Command \"{EscapeCmd(cmds[0])}\"";
        string extraTabs = cmds.Length > 1
            ? " ; " + string.Join(" ; ", cmds.Skip(1).Select(c => $"new-tab -d \"{item.Path}\" pwsh -NoExit -Command \"{EscapeCmd(c)}\""))
            : "";

        return Process.Start(new ProcessStartInfo
        {
            FileName         = "wt.exe",
            Arguments        = firstTab + extraTabs,
            UseShellExecute  = false,
        });
    }

    private static Process? LaunchObsidian(AppItem item)
    {
        // Find vault name from path
        string vaultName = Path.GetFileName(item.Path.TrimEnd('\\', '/'));
        string uri       = $"obsidian://open?vault={Uri.EscapeDataString(vaultName)}";
        return Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static string ResolveBrowser(string? browser)
    {
        if (string.IsNullOrEmpty(browser) || browser == "default") return string.Empty;

        // Map browser identifiers to executable names
        return browser switch
        {
            "msedge"  => FindBrowserPath("msedge.exe",  @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
            "chrome"  => FindBrowserPath("chrome.exe",  @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            "firefox" => FindBrowserPath("firefox.exe", @"C:\Program Files\Mozilla Firefox\firefox.exe"),
            "brave"   => FindBrowserPath("brave.exe",   @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"),
            _         => browser // Custom path
        };
    }

    private static string FindBrowserPath(string exeName, string defaultPath)
    {
        if (File.Exists(defaultPath)) return defaultPath;
        // Try PATH
        string? inPath = Environment.GetEnvironmentVariable("PATH")
            ?.Split(';')
            .Select(p => Path.Combine(p, exeName))
            .FirstOrDefault(File.Exists);
        return inPath ?? string.Empty;
    }

    private static string EscapeCmd(string cmd) => cmd.Replace("\"", "\\\"");
}
