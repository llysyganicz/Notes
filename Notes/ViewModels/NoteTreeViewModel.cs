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
    IRecipient<WorkspaceChangedMessage>,
    IRecipient<NewNoteRequestedMessage>
{
    private readonly IMessenger _messenger;
    private readonly IWorkspaceScanner _scanner;
    private readonly NoteTreeBuilder _treeBuilder;
    private readonly INoteDeleter _noteDeleter;
    private readonly IConfirmDialogService _confirmDialog;
    private readonly INewNoteNameValidator _newNoteValidator;
    private readonly INewNoteDialogService _newNoteDialog;
    private readonly INoteFileService _fileService;

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
        IConfirmDialogService confirmDialog,
        INewNoteNameValidator newNoteValidator,
        INewNoteDialogService newNoteDialog,
        INoteFileService fileService)
    {
        _messenger = messenger;
        _scanner = scanner;
        _treeBuilder = treeBuilder;
        _noteDeleter = noteDeleter;
        _confirmDialog = confirmDialog;
        _newNoteValidator = newNoteValidator;
        _newNoteDialog = newNoteDialog;
        _fileService = fileService;

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

    public async void Receive(NewNoteRequestedMessage message)
    {
        try
        {
            await HandleNewNote();
        }
        catch
        {
            // async void recipients must not throw onto the SynchronizationContext.
        }
    }

    private async Task HandleNewNote()
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var workspace = _workspacePath;
        var parentRelative = ResolveParentRelativePath(SelectedNode);
        var display = string.IsNullOrEmpty(parentRelative) ? "workspace root" : parentRelative;

        var entered = await _newNoteDialog.PromptForName(
            display,
            raw => _newNoteValidator.Validate(raw, workspace, parentRelative) is NoteNameResult.Failure failure
                ? failure.Error
                : null);
        if (entered is null)
        {
            return;
        }

        if (_newNoteValidator.Validate(entered, workspace, parentRelative) is not NoteNameResult.Success success)
        {
            return;
        }

        _fileService.Save(success.AbsolutePath, string.Empty);

        await LoadTreeCommand.ExecuteAsync(null);

        var newRelativePath = string.IsNullOrEmpty(parentRelative)
            ? success.FileName
            : parentRelative + "/" + success.FileName;
        var match = FindNode(Root, newRelativePath);
        if (match is not null)
        {
            SelectedNode = match;
        }
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

    private static string ResolveParentRelativePath(NoteTreeNode? selected)
    {
        if (selected is null)
        {
            return string.Empty;
        }

        if (selected.Kind == NoteNodeKind.Folder)
        {
            return selected.RelativePath;
        }

        var relative = selected.RelativePath;
        var lastSlash = relative.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : relative.Substring(0, lastSlash);
    }

    private static NoteTreeNode? FindNode(NoteTreeNode? node, string relativePath)
    {
        if (node is null)
        {
            return null;
        }

        if (node.RelativePath == relativePath)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNode(child, relativePath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
