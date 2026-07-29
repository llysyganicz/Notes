using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Core.Messaging;
using Notes.Core.Models;
using Notes.Services;
using Notes.Core.Services;

namespace Notes.ViewModels;

public sealed partial class NoteEditorViewModel :
    ObservableObject,
    IRecipient<WorkspaceChangedMessage>,
    IRecipient<NoteSelectedMessage>,
    IRecipient<NoteDeletedMessage>,
    IRecipient<TogglePreviewRequestedMessage>
{
    private readonly IMessenger _messenger;
    private readonly INoteFileService _fileService;
    private readonly IAutoSaveScheduler _scheduler;
    private readonly ITemplateService _templateService;

    private string? _workspacePath;
    private NoteTreeNode? _currentNote;
    private string _currentEditorText = string.Empty;

    [ObservableProperty]
    private string _loadedText = string.Empty;

    [ObservableProperty]
    private EditorPaneState _paneState = EditorPaneState.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    public NoteEditorViewModel(
        IMessenger messenger,
        INoteFileService fileService,
        IAutoSaveScheduler scheduler,
        ITemplateService templateService)
    {
        _messenger = messenger;
        _fileService = fileService;
        _scheduler = scheduler;
        _templateService = templateService;

        _scheduler.OnSave += DoSave;
        _messenger.RegisterAll(this);
    }

    internal event Action<string>? InsertAtCaretRequested;

    internal void ApplyCaretInsert(string body) => InsertAtCaretRequested?.Invoke(body);

    public bool IsEmpty => PaneState == EditorPaneState.Empty;
    public bool IsEditing => PaneState == EditorPaneState.Editing;
    public bool IsPreviewing => PaneState == EditorPaneState.Previewing;

    partial void OnPaneStateChanged(EditorPaneState value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsPreviewing));
    }

    public void Receive(WorkspaceChangedMessage message)
    {
        _scheduler.Flush();
        _workspacePath = message.WorkspacePath;
        _currentNote = null;
        _currentEditorText = string.Empty;
        LoadedText = string.Empty;
        PaneState = EditorPaneState.Empty;
    }

    public void Receive(NoteSelectedMessage message)
    {
        var node = message.Node;
        if (node is not null
            && node.Kind == NoteNodeKind.File
            && _currentNote is not null
            && node.RelativePath == _currentNote.RelativePath)
        {
            return;
        }

        _scheduler.Flush();

        if (node is null || node.Kind != NoteNodeKind.File || string.IsNullOrEmpty(_workspacePath))
        {
            _currentNote = null;
            _currentEditorText = string.Empty;
            LoadedText = string.Empty;
            PaneState = EditorPaneState.Empty;
            return;
        }

        var relative = node.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(_workspacePath, relative);
        string content;
        try
        {
            content = _fileService.Read(absolutePath);
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"Note read failed for '{absolutePath}': {ex.Message}");
            content = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"Note read denied for '{absolutePath}': {ex.Message}");
            content = string.Empty;
        }

        _currentNote = node;
        _currentEditorText = content;
        LoadedText = content;
        PaneState = EditorPaneState.Editing;
    }

    public void Receive(NoteDeletedMessage message)
    {
        if (_currentNote?.RelativePath != message.RelativePath)
        {
            return;
        }

        _scheduler.Cancel();
        _currentNote = null;
        _currentEditorText = string.Empty;
        LoadedText = string.Empty;
        PaneState = EditorPaneState.Empty;
    }

    public void Receive(TogglePreviewRequestedMessage message)
    {
        switch (PaneState)
        {
            case EditorPaneState.Editing:
                PreviewText = _currentEditorText;
                PaneState = EditorPaneState.Previewing;
                break;
            case EditorPaneState.Previewing:
                PaneState = EditorPaneState.Editing;
                break;
        }
    }

    public void OnEditorTextChanged(string text)
    {
        _currentEditorText = text;
        if (_currentNote is not null)
        {
            _scheduler.Bump();
        }
    }

    [RelayCommand(CanExecute = nameof(IsEditing))]
    private async Task InsertFromTemplate()
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var body = await _templateService.RenderForInsert(_workspacePath);
        if (!string.IsNullOrEmpty(body))
        {
            ApplyCaretInsert(body);
        }
    }

    private void DoSave()
    {
        if (_currentNote is null || string.IsNullOrEmpty(_workspacePath))
        {
            return;
        }

        var relative = _currentNote.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(_workspacePath, relative);
        try
        {
            _fileService.Save(absolutePath, _currentEditorText);
            _messenger.Send(new NoteSavedMessage(_currentNote.RelativePath, _currentEditorText));
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"Auto-save failed for '{absolutePath}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"Auto-save denied for '{absolutePath}': {ex.Message}");
        }
    }
}
