using System;
using System.IO;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NewNoteNameValidatorTests : IDisposable
{
    private readonly string _workspace;
    private readonly NewNoteNameValidator _sut = new();

    public NewNoteNameValidatorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "notes-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void Validate_WhenInputIsEmpty_ReturnsError()
    {
        var result = _sut.Validate("", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name cannot be empty", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputIsWhitespace_ReturnsError()
    {
        var result = _sut.Validate("   ", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name cannot be empty", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputContainsForwardSlash_ReturnsError()
    {
        var result = _sut.Validate("foo/bar.md", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputContainsBackslash_ReturnsError()
    {
        var result = _sut.Validate("foo\\bar.md", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputContainsInvalidFileNameChar_ReturnsError()
    {
        var result = _sut.Validate("foo\0bar.md", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputLacksExtension_AppendsMdSuffix()
    {
        var result = _sut.Validate("ideas", _workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.md", success.FileName);
    }

    [Fact]
    public void Validate_WhenInputHasMdSuffix_PreservesItExactly()
    {
        var result = _sut.Validate("ideas.md", _workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.md", success.FileName);
    }

    [Fact]
    public void Validate_WhenInputHasUppercaseMdSuffix_DoesNotDoubleAppend()
    {
        var result = _sut.Validate("ideas.MD", _workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.MD", success.FileName);
    }

    [Fact]
    public void Validate_WhenFileAlreadyExists_ReturnsError()
    {
        File.WriteAllText(Path.Combine(_workspace, "existing.md"), "x");

        var result = _sut.Validate("existing", _workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("A note with that name already exists", failure.Error);
    }

    [Fact]
    public void Validate_WhenInputIsValidAndUnique_ReturnsNormalizedFileName()
    {
        var result = _sut.Validate("fresh", _workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("fresh.md", success.FileName);
    }

    [Fact]
    public void Validate_WhenParentSubfolderUsed_ResolvesPathThroughParent()
    {
        var sub = Path.Combine(_workspace, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "dup.md"), "x");

        var resultDup = _sut.Validate("dup", _workspace, "sub");
        var resultFresh = _sut.Validate("fresh", _workspace, "sub");

        var failure = Assert.IsType<NoteNameResult.Failure>(resultDup);
        Assert.Equal("A note with that name already exists", failure.Error);

        var success = Assert.IsType<NoteNameResult.Success>(resultFresh);
        Assert.Equal("fresh.md", success.FileName);
    }
}
