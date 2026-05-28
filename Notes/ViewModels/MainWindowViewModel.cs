using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFolderPicker _folderPicker;
    private readonly IWorkspaceScanner _scanner;
    private readonly NoteTreeBuilder _treeBuilder;
    private readonly INoteDeleter _noteDeleter;
    private readonly IConfirmDialogService _confirmDialog;

    [ObservableProperty]
    private string? _workspacePath;

    [ObservableProperty]
    private NoteTreeNode? _root;

    public MainWindowViewModel(
        ISettingsService settingsService,
        IFolderPicker folderPicker,
        IWorkspaceScanner scanner,
        NoteTreeBuilder treeBuilder,
        INoteDeleter noteDeleter,
        IConfirmDialogService confirmDialog)
    {
        _settingsService = settingsService;
        _folderPicker = folderPicker;
        _scanner = scanner;
        _treeBuilder = treeBuilder;
        _noteDeleter = noteDeleter;
        _confirmDialog = confirmDialog;
    }

    [RelayCommand]
    private async Task ChangeWorkspace()
    {
        var picked = await _folderPicker.PickFolder();
        if (picked is null)
        {
            return;
        }

        _settingsService.Save(new AppSettings(picked));
        WorkspacePath = picked;
        await LoadTreeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void Exit()
    {
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    [RelayCommand]
    private Task LoadTree()
    {
        var workspace = WorkspacePath;
        if (string.IsNullOrEmpty(workspace))
        {
            Root = null;
            return Task.CompletedTask;
        }

        var paths = _scanner.ScanMarkdownFiles(workspace);
        Root = _treeBuilder.Build(paths);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNote))]
    private async Task DeleteNote(NoteTreeNode? node)
    {
        if (node is null || node.Kind != NoteNodeKind.File || string.IsNullOrEmpty(WorkspacePath))
        {
            return;
        }

        var confirmed = await _confirmDialog.Confirm(
            "Delete note",
            $"Are you sure you want to delete\n{node.RelativePath}?");
        if (!confirmed)
        {
            return;
        }

        var relative = node.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(WorkspacePath, relative);
        _noteDeleter.Delete(absolutePath);
        await LoadTreeCommand.ExecuteAsync(null);
    }

    private static bool CanDeleteNote(NoteTreeNode? node) =>
        node?.Kind == NoteNodeKind.File;
}
