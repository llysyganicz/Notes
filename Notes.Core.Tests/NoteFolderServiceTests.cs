using System.IO.Abstractions.TestingHelpers;
using Notes.Core.Services;
using NSubstitute;
using Xunit;

namespace Notes.Core.Tests;

public sealed class NoteFolderServiceTests
{
    private const string Root = "/workspace";

    private static (NoteFolderService svc, MockFileSystem fs) Build()
    {
        var mockFs = new MockFileSystem();
        var settings = Substitute.For<ISettingsService>();
        settings.CurrentWorkspacePath.Returns(Root);
        var guard = new PathGuard(settings);
        return (new NoteFolderService(mockFs, guard), mockFs);
    }

    [Fact]
    public void Create_WhenPathInsideWorkspace_CreatesDirectory()
    {
        var (svc, fs) = Build();
        const string path = Root + "/sub/child";

        svc.Create(path);

        Assert.True(fs.Directory.Exists(path));
    }

    [Theory]
    [InlineData("/etc/evil")]
    [InlineData("/workspace-evil/sub")]
    public void Create_WhenPathOutsideWorkspace_ThrowsAndCreatesNothing(string outsidePath)
    {
        var (svc, fs) = Build();

        Assert.Throws<PathContainmentException>(() => svc.Create(outsidePath));

        // Independent oracle: the guard must abort before CreateDirectory runs.
        Assert.False(fs.Directory.Exists(outsidePath));
    }
}
