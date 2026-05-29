using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class NoteTreeViewModel :
    ObservableObject,
    IRecipient<WorkspaceChangedMessage>
{
    private readonly IMessenger _messenger;
    private readonly IWorkspaceScanner _scanner;
    private readonly NoteTreeBuilder _treeBuilder;
    private readonly INoteDeleter _noteDeleter;
    private readonly IConfirmDialogService _confirmDialog;

    private string? _workspacePath;

    [ObservableProperty]
    private NoteTreeNode? _root;

    [ObservableProperty]
    private NoteTreeNode? _selectedNode;

    public NoteTreeViewModel(
        IMessenger messenger,
        IWorkspaceScanner scanner,
        NoteTreeBuilder treeBuilder,
        INoteDeleter noteDeleter,
        IConfirmDialogService confirmDialog)
    {
        _messenger = messenger;
        _scanner = scanner;
        _treeBuilder = treeBuilder;
        _noteDeleter = noteDeleter;
        _confirmDialog = confirmDialog;

        _messenger.RegisterAll(this);
    }

    partial void OnSelectedNodeChanged(NoteTreeNode? value)
    {
        _messenger.Send(new NoteSelectedMessage(value));
    }

    public void Receive(WorkspaceChangedMessage message)
    {
        _workspacePath = message.WorkspacePath;
        SelectedNode = null;
        _ = LoadTreeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private Task LoadTree()
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            Root = null;
            return Task.CompletedTask;
        }

        var paths = _scanner.ScanMarkdownFiles(_workspacePath);
        Root = _treeBuilder.Build(paths);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNote))]
    private async Task DeleteNote(NoteTreeNode? node)
    {
        if (node is null || node.Kind != NoteNodeKind.File || string.IsNullOrEmpty(_workspacePath))
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
        var absolutePath = Path.Combine(_workspacePath, relative);
        _noteDeleter.Delete(absolutePath);
        _messenger.Send(new NoteDeletedMessage(node.RelativePath));
        await LoadTreeCommand.ExecuteAsync(null);
    }

    private static bool CanDeleteNote(NoteTreeNode? node) =>
        node?.Kind == NoteNodeKind.File;
}
