using System;
using System.IO;
using Notes.Models;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Notes_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string ConfigPath() => Path.Combine(_tempDir, "settings.json");

    [Fact]
    public void Load_returns_Empty_when_file_missing()
    {
        var service = new SettingsService(ConfigPath());

        var result = service.Load();

        Assert.Same(AppSettings.Empty, result);
    }

    [Fact]
    public void Load_returns_Empty_when_json_is_malformed()
    {
        File.WriteAllText(ConfigPath(), "{ this is not valid json");
        var service = new SettingsService(ConfigPath());

        var result = service.Load();

        Assert.Null(result.WorkspacePath);
    }

    [Fact]
    public void Save_then_Load_round_trips_workspace_path()
    {
        var service = new SettingsService(ConfigPath());
        var original = new AppSettings(WorkspacePath: "/home/user/notes");

        service.Save(original);
        var loaded = service.Load();

        Assert.Equal("/home/user/notes", loaded.WorkspacePath);
    }

    [Fact]
    public void Save_creates_parent_directory_when_missing()
    {
        var nestedPath = Path.Combine(_tempDir, "deeply", "nested", "settings.json");
        var service = new SettingsService(nestedPath);

        service.Save(new AppSettings("/x"));

        Assert.True(File.Exists(nestedPath));
    }

}
