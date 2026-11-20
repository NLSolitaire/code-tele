using System;
using System.IO;
using System.Text.Json;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _currentSettings;
    private readonly object _lock = new();

    public AppSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
    }

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        // SettingsService est partagé par le conteneur de App.xaml.cs ; il charge le contrat AppSettings
        // avant que SettingsViewModel et MainViewModel n'en lisent ou n'en modifient les valeurs.
        _settingsFilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        _currentSettings = LoadSettings();
    }

    public AppSettings LoadSettings()
    {
        lock (_lock)
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }

            var defaultSettings = new AppSettings();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }
    }

    public void SaveSettings(AppSettings newSettings)
    {
        lock (_lock)
        {
            _currentSettings = newSettings;
            try
            {
                // L'écriture temporaire puis le remplacement évitent de laisser un JSON partiel
                // si l'application est interrompue pendant la sauvegarde des paramètres.
                string tempPath = _settingsFilePath + ".tmp";
                string json = JsonSerializer.Serialize(newSettings, _jsonOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _settingsFilePath, overwrite: true);
            }
            catch
            {
                // Silently handle
            }
        }

        SettingsChanged?.Invoke(_currentSettings);
    }
}
