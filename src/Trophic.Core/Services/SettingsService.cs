using System.Text.Json;
using Trophic.Core.Interfaces;

namespace Trophic.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private SettingsData _data = new();

    public string LanguageCode
    {
        get => _data.LanguageCode;
        set => _data.LanguageCode = value;
    }

    public string? LastBrowseDirectory
    {
        get => _data.LastBrowseDirectory;
        set => _data.LastBrowseDirectory = value;
    }

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        Load();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    public void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                _data = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
            catch (Exception) // JSON parse or I/O failure — use defaults
            {
                _data = new SettingsData();
            }
        }
    }

    private sealed class SettingsData
    {
        public string LanguageCode { get; set; } = "en"; // Default: English
        public string? LastBrowseDirectory { get; set; }
    }
}
