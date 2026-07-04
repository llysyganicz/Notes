using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Notes.ViewModels;
using Xunit;

namespace Notes.E2ETests;

public sealed class SmokeTests : E2ETestBase
{
    [AvaloniaFact]
    public void MainWindow_WhenInitialized_ShowsTree()
    {
        var tree = FindControl<TreeView>();

        Assert.NotNull(tree);
        Assert.True(tree.IsVisible);
        Assert.NotNull(Services.GetRequiredService<NoteTreeViewModel>().Root);
    }
}
