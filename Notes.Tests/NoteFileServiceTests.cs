using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NoteFileServiceTests
{
    private readonly MockFileSystem _mockFs = new();
    private readonly NoteFileService _service;

    public NoteFileServiceTests()
    {
        _service = new NoteFileService(_mockFs);
    }

    [Fact]
    public void Read_WhenFileMissing_ReturnsEmpty()
    {
        var result = _service.Read("/does-not-exist.md");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Read_WhenFileExists_ReturnsContent()
    {
        _mockFs.AddFile("/note.md", new MockFileData("# Hello\n\nWorld with łódź 漢字"));

        var result = _service.Read("/note.md");

        Assert.Equal("# Hello\n\nWorld with łódź 漢字", result);
    }

    // Production writes UTF-8 no-BOM by .NET 10 platform default (File.WriteAllText), relied on
    // after the NoteFileService encoding simplification. We assert no-BOM on MockFileSystem rather
    // than re-running it against the real disk: per the project's "never touch real disk in tests"
    // rule this stays hermetic, and the platform default is a documented .NET guarantee — not
    // re-proven here. ASCII "hello" makes the expected bytes encoder-independent literals.
    [Fact]
    public void Save_WhenCalled_WritesUtf8WithoutBom()
    {
        _service.Save("/note.md", "hello");

        var bytes = _mockFs.GetFile("/note.md").Contents;
        Assert.Equal(Encoding.UTF8.GetBytes("hello"), bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void Save_WhenCalledTwice_OverwritesContent()
    {
        _service.Save("/note.md", "first");
        _service.Save("/note.md", "second");

        Assert.Equal("second", _service.Read("/note.md"));
    }

    [Fact]
    public void Save_WhenFollowedByRead_RoundtripsContent()
    {
        _service.Save("/note.md", "round trip with 漢字 and emoji 🎯");
        var result = _service.Read("/note.md");

        Assert.Equal("round trip with 漢字 and emoji 🎯", result);
    }

    [Fact]
    public void Save_WhenFollowedByRead_RoundtripsFrontmatterAndNonAsciiContent()
    {
        var content = "---\ntitle: Łódź trip\ntags: [travel, 日本]\n---\n\nVisited 東京 and had great 寿司. Emoji: 🎯🚀";

        _service.Save("/note.md", content);
        var result = _service.Read("/note.md");

        Assert.Equal(content, result);
    }

    [Fact]
    public void Save_WhenCalledWithLfContent_PreservesRawLfBytes()
    {
        _service.Save("/lf.md", "line one\nline two\nline three");

        var storedBytes = _mockFs.GetFile("/lf.md").Contents;
        Assert.DoesNotContain((byte)'\r', storedBytes);
        Assert.Equal(2, storedBytes.Count(b => b == '\n'));
    }

    [Fact]
    public void Save_WhenCalledWithCrlfContent_PreservesRawCrlfBytes()
    {
        _service.Save("/crlf.md", "line one\r\nline two\r\nline three");

        var storedBytes = _mockFs.GetFile("/crlf.md").Contents;
        var crlfCount = 0;
        for (var i = 0; i < storedBytes.Length - 1; i++)
            if (storedBytes[i] == '\r' && storedBytes[i + 1] == '\n') crlfCount++;
        Assert.Equal(2, crlfCount);
    }

    [Fact]
    public void Read_WhenFileHasBomPrefix_ReturnsBomStrippedContent()
    {
        var textContent = "# Note with BOM\n\nContent here.";
        var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF };
        var fileBytes = bomBytes.Concat(Encoding.UTF8.GetBytes(textContent)).ToArray();
        _mockFs.AddFile("/bom.md", new MockFileData(fileBytes));

        var result = _service.Read("/bom.md");

        Assert.Equal(textContent, result);
    }
}
