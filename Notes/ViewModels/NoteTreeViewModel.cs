using System.Collections.Generic;
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
    IRecipient<NewNoteRequestedMessage>,
    IRecipient<NewFolderRequestedMessage>
{
    private readonly IMessenger _messenger;
    private readonly IWorkspaceScanner _scanner;
    private readonly NoteTreeBuilder _treeBuilder;
    private readonly INoteDeleter _noteDeleter;
    private readonly IConfirmDialogService _confirmDialog;
    private readonly INameValidator _nameValidator;
    private readonly INewNoteDialogService _newNoteDialog;
    private readonly INoteFileService _fileService;
    private readonly INoteFolderService _folderService;

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
        INameValidator nameValidator,
        INewNoteDialogService newNoteDialog,
        INoteFileService fileService,
        INoteFolderService folderService)
    {
        _messenger = messenger;
        _scanner = scanner;
        _treeBuilder = treeBuilder;
        _noteDeleter = noteDeleter;
        _confirmDialog = confirmDialog;
        _nameValidator = nameValidator;
        _newNoteDialog = newNoteDialog;
        _fileService = fileService;
        _folderService = folderService;

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

    public async void Receive(NewFolderRequestedMessage message)
    {
        try
        {
            await HandleNewFolder(ResolveParentRelativePath(SelectedNode));
        }
        catch
        {
            // async void recipients must not throw onto the SynchronizationContext.
        }
    }

    [RelayCommand]
    private Task NewFolder(NoteTreeNode? node) =>
        HandleNewFolder(ResolveParentRelativePath(node));

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
            raw => _nameValidator.ValidateNoteName(raw, workspace, parentRelative) is NoteNameResult.Failure failure
                ? failure.Error
                : null);
        if (entered is null)
        {
            return;
        }

        if (_nameValidator.ValidateNoteName(entered, workspace, parentRelative) is not NoteNameResult.Success success)
        {
            return;
        }

        var newRelativePath = string.IsNullOrEmpty(parentRelative)
            ? success.FileName
            : parentRelative + "/" + success.FileName;

        _fileService.Save(success.AbsolutePath, string.Empty);
        _messenger.Send(new NoteSavedMessage(newRelativePath, string.Empty));

        await LoadTreeCommand.ExecuteAsync(null);

        var match = FindNode(Root, newRelativePath);
        if (match is not null)
        {
            SelectedNode = match;
        }
    }

    private async Task HandleNewFolder(string parentRelative)
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var workspace = _workspacePath;
        var display = string.IsNullOrEmpty(parentRelative) ? "workspace root" : parentRelative;

        var entered = await _newNoteDialog.PromptForName(
            display,
            raw => _nameValidator.ValidateFolderName(raw, workspace, parentRelative) is NoteNameResult.Failure failure
                ? failure.Error
                : null);
        if (entered is null)
        {
            return;
        }

        if (_nameValidator.ValidateFolderName(entered, workspace, parentRelative) is not NoteNameResult.Success success)
        {
            return;
        }

        var newRelativePath = string.IsNullOrEmpty(parentRelative)
            ? success.FileName
            : parentRelative + "/" + success.FileName;

        _folderService.Create(success.AbsolutePath);

        await LoadTreeCommand.ExecuteAsync(null);

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
        Root = _treeBuilder.Build(_workspacePath, paths);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNote))]
    private async Task DeleteNote(NoteTreeNode? node)
    {
        if (node is null || !CanDeleteNote(node) || string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var relative = node.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(_workspacePath, relative);

        if (node.Kind == NoteNodeKind.Folder)
        {
            var confirmedFolder = await _confirmDialog.Confirm(
                "Delete folder",
                $"Are you sure you want to delete the folder\n{node.RelativePath}\nand all notes inside it?");
            if (!confirmedFolder)
            {
                return;
            }

            _noteDeleter.DeleteFolder(absolutePath);
            foreach (var file in DescendantFileNodes(node))
            {
                _messenger.Send(new NoteDeletedMessage(file.RelativePath));
            }

            await LoadTreeCommand.ExecuteAsync(null);
            return;
        }

        var confirmed = await _confirmDialog.Confirm(
            "Delete note",
            $"Are you sure you want to delete\n{node.RelativePath}?");
        if (!confirmed)
        {
            return;
        }

        _noteDeleter.Delete(absolutePath);
        _messenger.Send(new NoteDeletedMessage(node.RelativePath));
        await LoadTreeCommand.ExecuteAsync(null);
    }

    // Deletable = anything but the synthetic root (the only node with an empty RelativePath).
    private static bool CanDeleteNote(NoteTreeNode? node) =>
        !string.IsNullOrEmpty(node?.RelativePath);

    private static IEnumerable<NoteTreeNode> DescendantFileNodes(NoteTreeNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == NoteNodeKind.File)
            {
                yield return child;
            }
            else
            {
                foreach (var descendant in DescendantFileNodes(child))
                {
                    yield return descendant;
                }
            }
        }
    }

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
