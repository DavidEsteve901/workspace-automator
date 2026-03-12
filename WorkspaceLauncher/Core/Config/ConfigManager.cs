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
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly string ConfigFileName = "mis_apps_config_v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented              = true,
        AllowTrailingCommas        = true,
        ReadCommentHandling        = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
    };

    private AppConfig _config = new();
    private string _configPath = string.Empty;
    public AppConfig Config => _config;
    public string ConfigPath => _configPath;

    private ConfigManager() { }

    public void Load(string? baseDir = null)
    {
        if (!string.IsNullOrEmpty(baseDir))
        {
            _configPath = Path.Combine(baseDir, ConfigFileName);
        }
        else if (string.IsNullOrEmpty(_configPath))
        {
            _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        }

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
            
            // Sincronizar categorías por si faltan en el JSON (retrocompatibilidad)
            SyncCategoryOrder();
            
            Console.WriteLine($"[Config] Loaded from: {_configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Load error: {ex.Message}");
            _config = new AppConfig();
        }
    }

    private void SyncCategoryOrder()
    {
        if (_config == null) return;
        
        var keys = _config.Apps.Keys.ToList();
        
        // Eliminar categorías que ya no están en los archivos
        _config.CategoryOrder.RemoveAll(k => !_config.Apps.ContainsKey(k));
        
        // Añadir nuevas categorías que no estén en el orden
        foreach (var k in keys)
        {
            if (!_config.CategoryOrder.Contains(k))
                _config.CategoryOrder.Add(k);
        }
    }

    public void ChangeConfigPath(string newPath)
    {
        _configPath = newPath;
        Load();
    }

    public void Save()
    {
        _saveLock.Wait();
        try
        {
            string json = JsonSerializer.Serialize(_config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Save error: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(_config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] SaveAsync error: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
