using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Core.Messaging;
using Notes.Core.Models;
using Notes.Services;
using Notes.Core.Services;
using Notes.Tests.Fakes;
using Notes.ViewModels;
using Notes.Core.ViewModels;
using Xunit;

namespace Notes.Tests;

public sealed class NoteEditorViewModelTests
{
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly InMemoryNoteFileService _fileService = new();
    private readonly StubAutoSaveScheduler _scheduler = new();

    private NoteEditorViewModel BuildSut() =>
        new(_messenger, _fileService, _scheduler);

    private static NoteTreeNode File(string relativePath, string name)
        => new(name, relativePath, NoteNodeKind.File, Array.Empty<NoteTreeNode>());

    private static NoteTreeNode Folder(string relativePath, string name)
        => new(name, relativePath, NoteNodeKind.Folder, Array.Empty<NoteTreeNode>());

    [Fact]
    public void Receive_WhenNoteSelectedMessageHasFile_LoadsContentAndSetsEditing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        var expectedPath = Path.Combine("/workspace", "sub", "note.md");
        _fileService.FilesByPath[expectedPath] = "loaded content";

        _messenger.Send(new NoteSelectedMessage(File("sub/note.md", "note.md")));

        Assert.Equal("loaded content", sut.LoadedText);
        Assert.Equal(EditorPaneState.Editing, sut.PaneState);
        Assert.True(sut.IsEditing);
    }

    [Fact]
    public void Receive_WhenNoteSelectedMessageHasFolder_ClearsState()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "hello";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));

        _messenger.Send(new NoteSelectedMessage(Folder("sub", "sub")));

        Assert.Equal(string.Empty, sut.LoadedText);
        Assert.Equal(EditorPaneState.Empty, sut.PaneState);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void Receive_WhenNoteDeletedMessageMatchesCurrent_ClearsState()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "hello";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));

        _messenger.Send(new NoteDeletedMessage("a.md"));

        Assert.Equal(string.Empty, sut.LoadedText);
        Assert.Equal(EditorPaneState.Empty, sut.PaneState);
        Assert.True(_scheduler.CancelCalled);
    }

    [Fact]
    public void Receive_WhenNoteDeletedMessageDoesNotMatchCurrent_LeavesStateUnchanged()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "hello";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));

        _messenger.Send(new NoteDeletedMessage("other.md"));

        Assert.Equal("hello", sut.LoadedText);
        Assert.Equal(EditorPaneState.Editing, sut.PaneState);
    }

    [Fact]
    public void Receive_WhenWorkspaceChangedMessage_ResetsState()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "hello";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));

        _messenger.Send(new WorkspaceChangedMessage("/other"));

        Assert.Equal(string.Empty, sut.LoadedText);
        Assert.Equal(EditorPaneState.Empty, sut.PaneState);
    }

    [Fact]
    public void OnEditorTextChanged_WhenCurrentNoteSet_BumpsScheduler()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = string.Empty;
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));
        _scheduler.BumpCount = 0;

        sut.OnEditorTextChanged("hello");

        Assert.Equal(1, _scheduler.BumpCount);
    }

    [Fact]
    public void OnSave_WhenRaised_PersistsCurrentEditorTextToFile()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = string.Empty;
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));
        sut.OnEditorTextChanged("new text");

        _scheduler.RaiseOnSave();

        var expectedPath = Path.Combine("/workspace", "a.md");
        Assert.Equal("new text", _fileService.FilesByPath[expectedPath]);
    }

    [Fact]
    public void OnSave_WhenRaisedWithNoCurrentNote_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath.Clear();

        _scheduler.RaiseOnSave();

        Assert.Empty(_fileService.FilesByPath);
    }

    [Fact]
    public void Receive_WhenTogglePreviewMessageInEditingState_CopiesTextToPreviewAndSwitches()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "on-disk";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));
        sut.OnEditorTextChanged("# live edits");

        _messenger.Send(new TogglePreviewRequestedMessage());

        Assert.Equal(EditorPaneState.Previewing, sut.PaneState);
        Assert.True(sut.IsPreviewing);
        Assert.Equal("# live edits", sut.PreviewText);
    }

    [Fact]
    public void Receive_WhenTogglePreviewMessageInPreviewingState_SwitchesBackToEditing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));
        _fileService.FilesByPath[Path.Combine("/workspace", "a.md")] = "hello";
        _messenger.Send(new NoteSelectedMessage(File("a.md", "a.md")));
        _messenger.Send(new TogglePreviewRequestedMessage());

        _messenger.Send(new TogglePreviewRequestedMessage());

        Assert.Equal(EditorPaneState.Editing, sut.PaneState);
        Assert.True(sut.IsEditing);
        Assert.Equal("hello", sut.LoadedText);
    }

    [Fact]
    public void Receive_WhenTogglePreviewMessageInEmptyState_RemainsEmpty()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));

        _messenger.Send(new TogglePreviewRequestedMessage());

        Assert.Equal(EditorPaneState.Empty, sut.PaneState);
        Assert.True(sut.IsEmpty);
    }

    private sealed class StubAutoSaveScheduler : IAutoSaveScheduler
    {
        private Action? _onSave;

        public int BumpCount { get; set; }
        public bool FlushCalled { get; private set; }
        public bool CancelCalled { get; private set; }

        public event Action OnSave
        {
            add => _onSave += value;
            remove => _onSave -= value;
        }

        public void Bump() => BumpCount++;
        public void Flush() => FlushCalled = true;
        public void Cancel() => CancelCalled = true;

        public void RaiseOnSave() => _onSave?.Invoke();
    }
}
