using System.IO;
using Notes.Core.Services;
using NSubstitute;
using Xunit;

namespace Notes.Core.Tests;

public sealed class PathGuardTests
{
    private static PathGuard Build(string? root)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.CurrentWorkspacePath.Returns(root);
        return new PathGuard(settings);
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenRootIsNull_Throws()
    {
        var guard = Build(null);

        Assert.Throws<PathContainmentException>(() => guard.EnsureWithinWorkspace("/workspace/note.md"));
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenRootIsEmpty_Throws()
    {
        var guard = Build("");

        Assert.Throws<PathContainmentException>(() => guard.EnsureWithinWorkspace("/workspace/note.md"));
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenPathIsDirectChildOfRoot_DoesNotThrow()
    {
        var guard = Build("/workspace");

        var ex = Record.Exception(() => guard.EnsureWithinWorkspace("/workspace/note.md"));

        Assert.Null(ex);
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenPathIsNestedUnderRoot_DoesNotThrow()
    {
        var guard = Build("/workspace");

        var ex = Record.Exception(() => guard.EnsureWithinWorkspace("/workspace/sub/deep/note.md"));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/workspace-evil/note.md")]
    [InlineData("/other/note.md")]
    public void EnsureWithinWorkspace_WhenPathIsOutsideRoot_Throws(string outsidePath)
    {
        var guard = Build("/workspace");

        Assert.Throws<PathContainmentException>(() => guard.EnsureWithinWorkspace(outsidePath));
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenPathSharesRootPrefixButEscapes_Throws()
    {
        // "/workspace" must not match "/workspace-evil" — separator-aware check
        var guard = Build("/workspace");

        Assert.Throws<PathContainmentException>(() => guard.EnsureWithinWorkspace("/workspace-evil/note.md"));
    }

    [Fact]
    public void EnsureWithinWorkspace_WhenPathContainsTraversal_Throws()
    {
        var guard = Build("/workspace");

        Assert.Throws<PathContainmentException>(() => guard.EnsureWithinWorkspace("/workspace/sub/../../etc/passwd"));
    }

    [Fact]
    public void EnsureWithinWorkspace_ThrowsPathContainmentException_WhichIsIoException()
    {
        // PathContainmentException must derive from IOException so NoteEditorViewModel catches it
        var guard = Build(null);

        var ex = Assert.ThrowsAny<IOException>(() => guard.EnsureWithinWorkspace("/any"));
        Assert.IsType<PathContainmentException>(ex);
    }
}
