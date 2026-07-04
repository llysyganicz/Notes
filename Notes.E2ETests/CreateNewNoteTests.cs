using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.Models;
using Notes.ViewModels;
using Notes.Views;
using Xunit;

namespace Notes.E2ETests;

public sealed class CreateNewNoteTests : E2ETestBase
{
    [AvaloniaFact]
    public async Task CreateNewNote_WhenNameProvided_SelectsNoteInTreeAndEditor()
    {
        var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
        mainViewModel.NewNoteCommand.Execute(null);

        var dialog = await WaitForWindowAsync<NewNoteDialog>();
        await SetTextBoxTextAsync(dialog, "NameInput", "ideas");
        await ClickButtonAsync(dialog, "CreateButton");

        var expectedPath = Path.Combine(WorkspacePath, "ideas.md");
        await WaitForConditionAsync(() => FileSystem.File.Exists(expectedPath));
        await WaitForConditionAsync(() => Services.GetRequiredService<NoteTreeViewModel>().SelectedNode?.Name == "ideas.md");

        Assert.True(FileSystem.File.Exists(expectedPath));
        Assert.Equal("ideas.md", Services.GetRequiredService<NoteTreeViewModel>().SelectedNode?.Name);
        Assert.Equal(string.Empty, GetEditorText());
        Assert.Equal(EditorPaneState.Editing, Services.GetRequiredService<NoteEditorViewModel>().PaneState);
    }

    [AvaloniaFact]
    public async Task CreateNewNote_WhenDialogCancelled_CreatesNoFile()
    {
        var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
        mainViewModel.NewNoteCommand.Execute(null);

        var dialog = await WaitForWindowAsync<NewNoteDialog>();
        await ClickButtonAsync(dialog, "CancelButton");

        await WaitForConditionAsync(() => !dialog.IsVisible);

        Assert.False(FileSystem.File.Exists(Path.Combine(WorkspacePath, "ideas.md")));
        Assert.Null(Services.GetRequiredService<NoteTreeViewModel>().SelectedNode);
        Assert.Equal(EditorPaneState.Empty, Services.GetRequiredService<NoteEditorViewModel>().PaneState);
    }

    [AvaloniaFact]
    public async Task CreateNewNote_WhenNameInvalid_CreateButtonDisabled()
    {
        var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();
        mainViewModel.NewNoteCommand.Execute(null);

        var dialog = await WaitForWindowAsync<NewNoteDialog>();
        await SetTextBoxTextAsync(dialog, "NameInput", "bad/name");

        var createButton = FindControl<Button>(dialog, "CreateButton");
        var errorText = FindControl<TextBlock>(dialog, "ErrorText");

        Assert.False(createButton.IsEnabled);
        Assert.True(errorText.IsVisible);

        await ClickButtonAsync(dialog, "CancelButton");
    }
}
