using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NameValidatorTests
{
    private const string Workspace = "/workspace";

    private static NameValidator BuildSut(MockFileSystem fileSystem) => new(fileSystem);

    #region Note name validation

    [Fact]
    public void ValidateNoteName_WhenInputIsEmpty_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name cannot be empty", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputIsWhitespace_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("   ", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name cannot be empty", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputContainsForwardSlash_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("foo/bar.md", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputContainsBackslash_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("foo\\bar.md", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputContainsInvalidFileNameChar_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("foo\0bar.md", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputLacksExtension_AppendsMdSuffix()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("ideas", Workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.md", success.FileName);
    }

    [Fact]
    public void ValidateNoteName_WhenInputHasMdSuffix_PreservesItExactly()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("ideas.md", Workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.md", success.FileName);
    }

    [Fact]
    public void ValidateNoteName_WhenInputHasUppercaseMdSuffix_DoesNotDoubleAppend()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("ideas.MD", Workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas.MD", success.FileName);
    }

    [Fact]
    public void ValidateNoteName_WhenFileAlreadyExists_ReturnsError()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Workspace, "existing.md"), new MockFileData("x"));

        var result = BuildSut(fileSystem).ValidateNoteName("existing", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("A note with that name already exists", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenInputIsValidAndUnique_ReturnsNormalizedFileName()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("fresh", Workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("fresh.md", success.FileName);
        Assert.Equal(Path.Combine(Workspace, "fresh.md"), success.AbsolutePath);
    }

    [Fact]
    public void ValidateNoteName_WhenParentSubfolderUsed_ResolvesPathThroughParent()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Workspace, "sub", "dup.md"), new MockFileData("x"));

        var resultDup = BuildSut(fileSystem).ValidateNoteName("dup", Workspace, "sub");
        var resultFresh = BuildSut(fileSystem).ValidateNoteName("fresh", Workspace, "sub");

        var failure = Assert.IsType<NoteNameResult.Failure>(resultDup);
        Assert.Equal("A note with that name already exists", failure.Error);

        var success = Assert.IsType<NoteNameResult.Success>(resultFresh);
        Assert.Equal("fresh.md", success.FileName);
        Assert.Equal(Path.Combine(Workspace, "sub", "fresh.md"), success.AbsolutePath);
    }

    #endregion

    #region Folder name validation

    [Fact]
    public void ValidateFolderName_WhenInputIsEmpty_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name cannot be empty", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenInputContainsForwardSlash_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("foo/bar", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenInputContainsBackslash_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("foo\\bar", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenInputContainsInvalidFileNameChar_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("foo\0bar", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenInputIsValidAndUnique_ReturnsFolderNameWithoutMdSuffix()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("ideas", Workspace, "");

        var success = Assert.IsType<NoteNameResult.Success>(result);
        Assert.Equal("ideas", success.FileName);
        Assert.Equal(Path.Combine(Workspace, "ideas"), success.AbsolutePath);
    }

    [Fact]
    public void ValidateFolderName_WhenDirectoryAlreadyExists_ReturnsError()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Path.Combine(Workspace, "existing"));

        var result = BuildSut(fileSystem).ValidateFolderName("existing", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("A folder with that name already exists", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenFileOfSameNameExists_ReturnsError()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Workspace, "existing"), new MockFileData("x"));

        var result = BuildSut(fileSystem).ValidateFolderName("existing", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("A folder with that name already exists", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenParentSubfolderUsed_ResolvesPathThroughParent()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Path.Combine(Workspace, "sub", "dup"));

        var resultDup = BuildSut(fileSystem).ValidateFolderName("dup", Workspace, "sub");
        var resultFresh = BuildSut(fileSystem).ValidateFolderName("fresh", Workspace, "sub");

        var failure = Assert.IsType<NoteNameResult.Failure>(resultDup);
        Assert.Equal("A folder with that name already exists", failure.Error);

        var success = Assert.IsType<NoteNameResult.Success>(resultFresh);
        Assert.Equal("fresh", success.FileName);
        Assert.Equal(Path.Combine(Workspace, "sub", "fresh"), success.AbsolutePath);
    }

    #endregion

    #region Dot / double-dot rejection

    [Fact]
    public void ValidateNoteName_WhenNameIsSingleDot_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName(".", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenNameIsDoubleDot_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("..", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    [Fact]
    public void ValidateFolderName_WhenNameIsDoubleDot_ReturnsError()
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName("..", Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name contains an invalid character", failure.Error);
    }

    #endregion

    #region Reserved device name rejection

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void ValidateNoteName_WhenNameIsReservedDeviceName_ReturnsError(string reserved)
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName(reserved, Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name is reserved and cannot be used", failure.Error);
    }

    [Theory]
    [InlineData("CON.md")]
    [InlineData("NUL.md")]
    [InlineData("COM3.md")]
    public void ValidateNoteName_WhenNameIsReservedDeviceNameWithMdExtension_ReturnsError(string reserved)
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName(reserved, Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name is reserved and cannot be used", failure.Error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("LPT1")]
    public void ValidateFolderName_WhenNameIsReservedDeviceName_ReturnsError(string reserved)
    {
        var result = BuildSut(new MockFileSystem()).ValidateFolderName(reserved, Workspace, "");

        var failure = Assert.IsType<NoteNameResult.Failure>(result);
        Assert.Equal("Name is reserved and cannot be used", failure.Error);
    }

    [Fact]
    public void ValidateNoteName_WhenNameIsNormalWord_StillPassesAfterReservedCheck()
    {
        var result = BuildSut(new MockFileSystem()).ValidateNoteName("console", Workspace, "");

        Assert.IsType<NoteNameResult.Success>(result);
    }

    #endregion
}
