using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.ViewModels;
using System.IO.Abstractions.TestingHelpers;
using Xunit;

namespace Notes.E2ETests;

public sealed class EditAndAutoSaveTests : E2ETestBase
{
    [AvaloniaFact]
    public async Task SelectNote_WhenClicked_LoadsContentIntoEditor()
    {
        var filePath = Path.Combine(WorkspacePath, "existing.md");
        FileSystem.AddFile(filePath, new MockFileData("# Hello\n\nworld"));
        await Services.GetRequiredService<NoteTreeViewModel>().LoadTreeCommand.ExecuteAsync(null);

        await SelectTreeItemAsync("existing.md");

        Assert.Equal("# Hello\n\nworld", GetEditorText());
        Assert.Equal(EditorPaneState.Editing, Services.GetRequiredService<NoteEditorViewModel>().PaneState);
    }

    [AvaloniaFact]
    public async Task EditNote_WhenTextChanged_AutoSavesAfterDelay()
    {
        var filePath = Path.Combine(WorkspacePath, "existing.md");
        FileSystem.AddFile(filePath, new MockFileData("# Hello\n\nworld"));
        await Services.GetRequiredService<NoteTreeViewModel>().LoadTreeCommand.ExecuteAsync(null);
        await SelectTreeItemAsync("existing.md");

        await SetEditorTextAsync("# Updated");
        FlushAutoSave();

        Assert.Equal("# Updated", FileSystem.File.ReadAllText(filePath));
    }

    [AvaloniaFact]
    public async Task EditNote_WhenSwitchedWithoutChange_KeepsOriginalContent()
    {
        var existingPath = Path.Combine(WorkspacePath, "existing.md");
        var otherPath = Path.Combine(WorkspacePath, "other.md");
        FileSystem.AddFile(existingPath, new MockFileData("# Hello\n\nworld"));
        FileSystem.AddFile(otherPath, new MockFileData("Other content"));
        await Services.GetRequiredService<NoteTreeViewModel>().LoadTreeCommand.ExecuteAsync(null);

        await SelectTreeItemAsync("existing.md");
        await SelectTreeItemAsync("other.md");

        Assert.Equal("# Hello\n\nworld", FileSystem.File.ReadAllText(existingPath));
    }
}
