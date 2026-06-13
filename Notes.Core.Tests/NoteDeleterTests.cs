using System.IO.Abstractions.TestingHelpers;
using Notes.Core.Services;
using NSubstitute;
using Xunit;

namespace Notes.Core.Tests;

public sealed class NoteDeleterTests
{
    private const string Root = "/workspace";

    private static (NoteDeleter svc, MockFileSystem fs) Build()
    {
        var mockFs = new MockFileSystem();
        var settings = Substitute.For<ISettingsService>();
        settings.CurrentWorkspacePath.Returns(Root);
        var guard = new PathGuard(settings);
        return (new NoteDeleter(mockFs, guard), mockFs);
    }

    [Fact]
    public void Delete_WhenPathInsideWorkspace_DeletesFile()
    {
        var (svc, fs) = Build();
        const string path = Root + "/note.md";
        fs.AddFile(path, new MockFileData("content"));

        svc.Delete(path);

        Assert.False(fs.File.Exists(path));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/workspace-evil/note.md")]
    [InlineData("/workspace/../etc/shadow")]
    public void Delete_WhenPathOutsideWorkspace_ThrowsAndLeavesFilesUntouched(string outsidePath)
    {
        var (svc, fs) = Build();
        fs.AddFile(outsidePath, new MockFileData("sensitive"));

        Assert.Throws<PathContainmentException>(() => svc.Delete(outsidePath));

        // Independent oracle: file must still exist
        Assert.True(fs.File.Exists(outsidePath));
    }

    [Fact]
    public void DeleteFolder_WhenPathInsideWorkspace_DeletesFolder()
    {
        var (svc, fs) = Build();
        const string folder = Root + "/sub";
        fs.AddDirectory(folder);
        fs.AddFile(folder + "/note.md", new MockFileData("x"));

        svc.DeleteFolder(folder);

        Assert.False(fs.Directory.Exists(folder));
    }

    [Theory]
    [InlineData("/other")]
    [InlineData("/workspace-evil")]
    public void DeleteFolder_WhenPathOutsideWorkspace_ThrowsAndLeavesDirectoryUntouched(string outsidePath)
    {
        var (svc, fs) = Build();
        fs.AddDirectory(outsidePath);

        Assert.Throws<PathContainmentException>(() => svc.DeleteFolder(outsidePath));

        Assert.True(fs.Directory.Exists(outsidePath));
    }
}
