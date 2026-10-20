namespace Orbit.Core.Services.Impl;

using System;
using System.IO;
using System.Text.Json;
using Orbit.Core.Models;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private OrbitSettings _currentSettings;

    public event Action<OrbitSettings>? SettingsChanged;

    public OrbitSettings CurrentSettings => _currentSettings;

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            _settingsFilePath = customPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var orbitDir = Path.Combine(appData, "Orbit");
            _settingsFilePath = Path.Combine(orbitDir, "settings.json");
        }

        _currentSettings = Load();
    }

    public OrbitSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<OrbitSettings>(json);
                if (loaded != null)
                {
                    _currentSettings = loaded;
                    return _currentSettings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Orbit] Error loading settings: {ex.Message}");
        }

        _currentSettings = new OrbitSettings();
        return _currentSettings;
    }

    public void Save(OrbitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, json);

            _currentSettings = settings;
            SettingsChanged?.Invoke(_currentSettings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Orbit] Error saving settings: {ex.Message}");
        }
    }
}
