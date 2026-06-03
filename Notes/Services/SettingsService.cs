using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using Notes.Models;

namespace Notes.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly IFileSystem _fileSystem;

    public string ConfigFilePath { get; }

    public SettingsService(IFileSystem fileSystem, string? configFilePath = null)
    {
        _fileSystem = fileSystem;
        ConfigFilePath = configFilePath ?? DefaultConfigFilePath();
    }

    public AppSettings Load()
    {
        if (!_fileSystem.File.Exists(ConfigFilePath))
        {
            return AppSettings.Empty;
        }

        try
        {
            var json = _fileSystem.File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Empty;
        }
        catch
        {
            return AppSettings.Empty;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = _fileSystem.Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.File.WriteAllText(ConfigFilePath, json);
    }

    private static string DefaultConfigFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Notes",
            "settings.json");
}
