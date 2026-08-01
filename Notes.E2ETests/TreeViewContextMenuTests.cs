using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.ViewModels;
using System.IO.Abstractions.TestingHelpers;
using Xunit;

namespace Notes.E2ETests;

public sealed class TreeViewContextMenuTests : E2ETestBase
{
    [AvaloniaFact]
    public async Task RightClickRow_WhenAimedAtEmptyRowArea_OpensContextMenu()
    {
        var filePath = Path.Combine(WorkspacePath, "existing.md");
        FileSystem.AddFile(filePath, new MockFileData("# Hello"));
        await Services.GetRequiredService<NoteTreeViewModel>().LoadTreeCommand.ExecuteAsync(null);

        var row = await WaitForRowAsync("existing.md");

        // Aim at the far-right edge of the row's stretched hit area, well past
        // the rendered text glyphs — this is the empty space that used to be
        // dead for right-click before the row-container fix.
        var localPoint = new Point(row.Bounds.Width - 2, row.Bounds.Height / 2);
        var windowPoint = row.TranslatePoint(localPoint, MainWindow)
            ?? throw new InvalidOperationException("Could not translate row point into window coordinates.");

        MainWindow.MouseDown(windowPoint, MouseButton.Right, RawInputModifiers.None);
        MainWindow.MouseUp(windowPoint, MouseButton.Right, RawInputModifiers.None);

        await WaitForConditionAsync(() => row.ContextMenu is { IsOpen: true });

        var menu = row.ContextMenu!;
        Assert.True(menu.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header).ToList();
        Assert.Contains("New Folder", headers);
        Assert.Contains("Delete", headers);
    }

    private async Task<Border> WaitForRowAsync(string headerText)
    {
        Border? row = null;
        await WaitForConditionAsync(() =>
        {
            row = MainWindow.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.ContextMenu is not null && b.DataContext is NoteTreeNode node && node.Name == headerText);
            return row is not null;
        });

        return row ?? throw new InvalidOperationException($"Tree row '{headerText}' was not found.");
    }
}
