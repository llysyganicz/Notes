using System;
using System.IO;
using System.Text.Json;
using Notes.Models;

namespace Notes.Services;

public sealed class SettingsService : ISettingsService
{
    public string ConfigFilePath { get; }

    public SettingsService()
        : this(DefaultConfigFilePath())
    {
    }

    public SettingsService(string configFilePath)
    {
        ConfigFilePath = configFilePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return AppSettings.Empty;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Empty;
        }
        catch
        {
            return AppSettings.Empty;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = ConfigFilePath + ".tmp";
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmp, json);
        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    private static string DefaultConfigFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Notes",
            "settings.json");
}
