using System;
using System.IO;
using System.IO.Abstractions;
using Notes.Core.Models;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

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
    public void Load_WhenFileMissing_ReturnsEmpty()
    {
        var service = new SettingsService(new FileSystem(), ConfigPath());

        var result = service.Load();

        Assert.Same(AppSettings.Empty, result);
    }

    [Fact]
    public void Load_WhenJsonMalformed_ReturnsEmpty()
    {
        File.WriteAllText(ConfigPath(), "{ this is not valid json");
        var service = new SettingsService(new FileSystem(), ConfigPath());

        var result = service.Load();

        Assert.Null(result.WorkspacePath);
    }

    [Fact]
    public void Load_WhenCalledAfterSave_ReturnsSavedSettings()
    {
        var service = new SettingsService(new FileSystem(), ConfigPath());
        var original = new AppSettings(WorkspacePath: "/home/user/notes");

        service.Save(original);
        var loaded = service.Load();

        Assert.Equal("/home/user/notes", loaded.WorkspacePath);
    }

    [Fact]
    public void Save_WhenParentDirectoryMissing_CreatesParentDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "deeply", "nested", "settings.json");
        var service = new SettingsService(new FileSystem(), nestedPath);

        service.Save(new AppSettings("/x"));

        Assert.True(File.Exists(nestedPath));
    }

}
