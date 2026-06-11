using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Notes.Core.Models;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

public sealed class SettingsServiceTests
{
    private readonly MockFileSystem _fs = new();
    private readonly string _tempDir = "/notes-tests";

    public SettingsServiceTests()
    {
        _fs.Directory.CreateDirectory(_tempDir);
    }

    private string ConfigPath() => Path.Combine(_tempDir, "settings.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmpty()
    {
        var service = new SettingsService(_fs, ConfigPath());

        var result = service.Load();

        Assert.Same(AppSettings.Empty, result);
    }

    [Fact]
    public void Load_WhenJsonMalformed_ReturnsEmpty()
    {
        _fs.File.WriteAllText(ConfigPath(), "{ this is not valid json");
        var service = new SettingsService(_fs, ConfigPath());

        var result = service.Load();

        Assert.Null(result.WorkspacePath);
    }

    [Fact]
    public void Load_WhenCalledAfterSave_ReturnsSavedSettings()
    {
        var service = new SettingsService(_fs, ConfigPath());
        var original = new AppSettings(WorkspacePath: "/home/user/notes");

        service.Save(original);
        var loaded = service.Load();

        Assert.Equal("/home/user/notes", loaded.WorkspacePath);
    }

    [Fact]
    public void Save_WhenParentDirectoryMissing_CreatesParentDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "deeply", "nested", "settings.json");
        var service = new SettingsService(_fs, nestedPath);

        service.Save(new AppSettings("/x"));

        Assert.True(_fs.File.Exists(nestedPath));
    }

}
