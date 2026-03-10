using System.IO;
using System.Text.Json;

namespace WorkspaceLauncher.Core.Config;

/// <summary>
/// Loads and saves mis_apps_config_v2.json.
/// Same schema as the Python version.
/// </summary>
public sealed class ConfigManager
{
    public static readonly ConfigManager Instance = new();

    private static readonly string ConfigFileName = "mis_apps_config_v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        AllowTrailingCommas    = true,
        ReadCommentHandling    = JsonCommentHandling.Skip,
    };

    private AppConfig _config = new();
    private string _configPath = string.Empty;

    public AppConfig Config => _config;

    private ConfigManager() { }

    public void Load(string? baseDir = null)
    {
        string dir  = baseDir ?? AppContext.BaseDirectory;
        _configPath = Path.Combine(dir, ConfigFileName);

        if (!File.Exists(_configPath))
        {
            _config = new AppConfig();
            Save();
            return;
        }

        try
        {
            string json = File.ReadAllText(_configPath);
            _config     = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Load error: {ex.Message}");
            _config = new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Save error: {ex.Message}");
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] SaveAsync error: {ex.Message}");
        }
    }
}
