using System;
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
    IRecipient<NewFolderRequestedMessage>,
    IRecipient<NewFromTemplateRequestedMessage>
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
    private readonly ITemplateCatalog _templateCatalog;
    private readonly ITemplatePickerDialogService _templatePickerDialog;
    private readonly ITemplateParser _templateParser;
    private readonly ITemplateFormDialogService _templateFormDialog;
    private readonly ITemplateRenderer _templateRenderer;

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
        INoteFolderService folderService,
        ITemplateCatalog templateCatalog,
        ITemplatePickerDialogService templatePickerDialog,
        ITemplateParser templateParser,
        ITemplateFormDialogService templateFormDialog,
        ITemplateRenderer templateRenderer)
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
        _templateCatalog = templateCatalog;
        _templatePickerDialog = templatePickerDialog;
        _templateParser = templateParser;
        _templateFormDialog = templateFormDialog;
        _templateRenderer = templateRenderer;

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

    public async void Receive(NewFromTemplateRequestedMessage message)
    {
        try
        {
            await HandleNewFromTemplate();
        }
        catch
        {
            // async void recipients must not throw onto the SynchronizationContext.
        }
    }

    [RelayCommand]
    private Task NewFolder(NoteTreeNode? node) =>
        HandleNewFolder(ResolveParentRelativePath(node));

    private Task HandleNewNote() => PromptNameAndSave(string.Empty);

    private async Task HandleNewFromTemplate()
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var templates = _templateCatalog.List();
        if (templates.Count == 0)
        {
            return;
        }

        var picked = await _templatePickerDialog.PickTemplate(templates);
        if (picked is null)
        {
            return;
        }

        var templateAbsolute = Path.Combine(
            _workspacePath,
            picked.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var templateText = _fileService.Read(templateAbsolute);

        var definition = _templateParser.Parse(templateText);

        IReadOnlyDictionary<string, string> values;
        if (definition.Fields.Count > 0)
        {
            var collected = await _templateFormDialog.CollectValues(definition);
            if (collected is null)
            {
                return;
            }

            values = collected;
        }
        else
        {
            values = new Dictionary<string, string>();
        }

        var rendered = _templateRenderer.Render(templateText, definition, values);

        await PromptNameAndSave(rendered);
    }

    private async Task PromptNameAndSave(string content)
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

        _fileService.Save(success.AbsolutePath, content);
        _messenger.Send(new NoteSavedMessage(newRelativePath, content));

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
            RefreshTemplateCatalog(Array.Empty<string>());
            return Task.CompletedTask;
        }

        var paths = _scanner.ScanMarkdownFiles(_workspacePath);
        Root = _treeBuilder.Build(_workspacePath, paths);
        RefreshTemplateCatalog(paths);
        return Task.CompletedTask;
    }

    // The tree reload is the single place the markdown file-set changes (workspace switch, note/
    // folder create, delete) — never autosave — so it is the natural point to refresh the template
    // cache, reusing the scan just performed. The catalog is updated before the notification fires,
    // so MainWindowViewModel reads a fresh HasAny() when it handles TemplatesChangedMessage.
    private void RefreshTemplateCatalog(IReadOnlyList<string> paths)
    {
        _templateCatalog.Load(paths);
        _messenger.Send(new TemplatesChangedMessage());
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
