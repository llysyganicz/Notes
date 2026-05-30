using System;
using System.IO;
using System.Text;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NoteFileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NoteFileService _service = new();

    public NoteFileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Notes_FileServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string PathOf(string fileName) => Path.Combine(_tempDir, fileName);

    [Fact]
    public void Read_WhenFileMissing_ReturnsEmpty()
    {
        var result = _service.Read(PathOf("does-not-exist.md"));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Read_WhenFileExists_ReturnsContent()
    {
        var path = PathOf("note.md");
        File.WriteAllText(path, "# Hello\n\nWorld with łódź 漢字");

        var result = _service.Read(path);

        Assert.Equal("# Hello\n\nWorld with łódź 漢字", result);
    }

    [Fact]
    public void Save_WhenCalled_WritesUtf8WithoutBom()
    {
        var path = PathOf("note.md");

        _service.Save(path, "hello");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(Encoding.UTF8.GetBytes("hello"), bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void Save_WhenCalledTwice_OverwritesContent()
    {
        var path = PathOf("note.md");

        _service.Save(path, "first");
        _service.Save(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WhenFollowedByRead_RoundtripsContent()
    {
        var path = PathOf("note.md");

        _service.Save(path, "round trip with 漢字 and emoji 🎯");
        var result = _service.Read(path);

        Assert.Equal("round trip with 漢字 and emoji 🎯", result);
    }
}
