using System.IO.Abstractions.TestingHelpers;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class OrphanedTempCleanerTests
{
    [Fact]
    public void Receive_WhenWorkspaceContainsOrphanedTemps_DeletesTempFiles()
    {
        const string root = "/workspace";
        var mockFs = new MockFileSystem();
        mockFs.AddFile($"{root}/note-a.md", new MockFileData("note a content"));
        mockFs.AddFile($"{root}/note-b.md", new MockFileData("note b content"));
        mockFs.AddFile($"{root}/note-a.md.tmp", new MockFileData("orphaned temp"));
        mockFs.AddFile($"{root}/sub/nested.md", new MockFileData("nested note"));
        mockFs.AddFile($"{root}/sub/nested.md.tmp", new MockFileData("nested orphaned temp"));

        var messenger = new StrongReferenceMessenger();
        _ = new OrphanedTempCleaner(mockFs, messenger);

        messenger.Send(new WorkspaceChangedMessage(root));

        // All .md notes survive
        Assert.True(mockFs.File.Exists($"{root}/note-a.md"));
        Assert.True(mockFs.File.Exists($"{root}/note-b.md"));
        Assert.True(mockFs.File.Exists($"{root}/sub/nested.md"));
        Assert.Equal("note a content", mockFs.File.ReadAllText($"{root}/note-a.md"));
        Assert.Equal("nested note", mockFs.File.ReadAllText($"{root}/sub/nested.md"));

        // All .md.tmp orphans removed
        Assert.False(mockFs.File.Exists($"{root}/note-a.md.tmp"));
        Assert.False(mockFs.File.Exists($"{root}/sub/nested.md.tmp"));
    }

    [Fact]
    public void Receive_WhenTempFileExistsOutsideRoot_DoesNotDeleteIt()
    {
        const string root = "/workspace";
        const string outsideTemp = "/other/stray.md.tmp";
        var mockFs = new MockFileSystem();
        mockFs.AddDirectory(root);
        mockFs.AddFile(outsideTemp, new MockFileData("outside temp"));

        var messenger = new StrongReferenceMessenger();
        _ = new OrphanedTempCleaner(mockFs, messenger);

        messenger.Send(new WorkspaceChangedMessage(root));

        Assert.True(mockFs.File.Exists(outsideTemp));
    }

    [Fact]
    public void Receive_WhenWorkspaceIsEmpty_DoesNotThrow()
    {
        const string root = "/workspace";
        var mockFs = new MockFileSystem();
        mockFs.AddDirectory(root);

        var messenger = new StrongReferenceMessenger();
        _ = new OrphanedTempCleaner(mockFs, messenger);

        var ex = Record.Exception(() => messenger.Send(new WorkspaceChangedMessage(root)));
        Assert.Null(ex);
    }
}
