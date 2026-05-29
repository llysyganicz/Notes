using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class NoteEditorViewModel :
    ObservableObject,
    IRecipient<WorkspaceChangedMessage>,
    IRecipient<NoteSelectedMessage>,
    IRecipient<NoteDeletedMessage>
{
    private readonly IMessenger _messenger;
    private readonly INoteFileService _fileService;
    private readonly IAutoSaveScheduler _scheduler;

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
        IAutoSaveScheduler scheduler)
    {
        _messenger = messenger;
        _fileService = fileService;
        _scheduler = scheduler;

        _scheduler.OnSave += DoSave;
        _messenger.RegisterAll(this);
    }

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
        _scheduler.Flush();

        var node = message.Node;
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
        var content = _fileService.Read(absolutePath);

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

    public void OnEditorTextChanged(string text)
    {
        _currentEditorText = text;
        if (_currentNote is not null)
        {
            _scheduler.Bump();
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
