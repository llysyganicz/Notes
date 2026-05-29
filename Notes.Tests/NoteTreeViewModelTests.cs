using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;
using Notes.Tests.Fakes;
using Notes.ViewModels;
using Xunit;

namespace Notes.Tests;

public sealed class NoteTreeViewModelTests : IDisposable
{
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly StubWorkspaceScanner _scanner = new();
    private readonly NoteTreeBuilder _treeBuilder = new();
    private readonly StubNoteDeleter _deleter = new();
    private readonly StubConfirmDialogService _confirm = new();
    private readonly NewNoteNameValidator _validator = new();
    private readonly StubNewNoteDialogService _newNoteDialog = new();
    private readonly InMemoryNoteFileService _fileService = new();
    private readonly string _workspace;

    public NoteTreeViewModelTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "notes-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private NoteTreeViewModel BuildSut() =>
        new(_messenger, _scanner, _treeBuilder, _deleter, _confirm, _validator, _newNoteDialog, _fileService);

    [Fact]
    public void Receive_WhenWorkspaceChangedMessage_LoadsTree()
    {
        _scanner.Paths = new List<string> { "a.md", "sub/b.md" };
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage("/workspace"));

        Assert.Equal("/workspace", _scanner.LastRoot);
        Assert.NotNull(sut.Root);
        Assert.Equal(2, sut.Root!.Children.Count);
    }

    [Fact]
    public void OnSelectedNodeChanged_WhenSet_PublishesNoteSelectedMessage()
    {
        var sut = BuildSut();
        NoteSelectedMessage? captured = null;
        _messenger.Register<NoteSelectedMessage>(this, (_, m) => captured = m);

        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        sut.SelectedNode = node;

        Assert.NotNull(captured);
        Assert.Same(node, captured!.Node);
    }

    [Fact]
    public async Task DeleteNote_WhenConfirmed_PublishesNoteDeletedMessage()
    {
        _scanner.Paths = new List<string> { "x.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));

        NoteDeletedMessage? captured = null;
        _messenger.Register<NoteDeletedMessage>(this, (_, m) => captured = m);

        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        await sut.DeleteNoteCommand.ExecuteAsync(node);

        Assert.NotNull(captured);
        Assert.Equal("x.md", captured!.RelativePath);
        var expected = Path.Combine("/workspace", "x.md");
        Assert.Contains(expected, _deleter.DeletedPaths);
    }

    [Fact]
    public async Task DeleteNote_WhenConfirmed_RefreshesTree()
    {
        _scanner.Paths = new List<string> { "x.md", "y.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage("/workspace"));

        _scanner.Paths = new List<string> { "y.md" };
        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        await sut.DeleteNoteCommand.ExecuteAsync(node);

        Assert.NotNull(sut.Root);
        var child = Assert.Single(sut.Root!.Children);
        Assert.Equal("y.md", child.Name);
    }

    public enum NewNoteSelection
    {
        NoSelection,
        FolderSelected,
        FileSelected,
    }

    [Theory]
    [InlineData(NewNoteSelection.NoSelection, "", "untitled.md")]
    [InlineData(NewNoteSelection.FolderSelected, "sub", "sub/untitled.md")]
    [InlineData(NewNoteSelection.FileSelected, "sub/x.md", "sub/untitled.md")]
    public void Receive_WhenNewNoteRequestedMessage_CreatesFileAtResolvedParent(
        NewNoteSelection selection,
        string selectedRelativePath,
        string expectedRelativePath)
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "sub"));
        File.WriteAllText(Path.Combine(_workspace, "sub", "x.md"), string.Empty);

        _scanner.Paths = selection == NewNoteSelection.NoSelection
            ? new List<string>()
            : new List<string> { "sub/x.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(_workspace));

        sut.SelectedNode = selection switch
        {
            NewNoteSelection.NoSelection => null,
            NewNoteSelection.FolderSelected => new NoteTreeNode("sub", "sub", NoteNodeKind.Folder, Array.Empty<NoteTreeNode>()),
            NewNoteSelection.FileSelected => new NoteTreeNode("x.md", selectedRelativePath, NoteNodeKind.File, Array.Empty<NoteTreeNode>()),
            _ => null,
        };

        _newNoteDialog.Response = "untitled";
        _scanner.Paths = selection == NewNoteSelection.NoSelection
            ? new List<string> { "untitled.md" }
            : new List<string> { "sub/x.md", "sub/untitled.md" };

        _messenger.Send(new NewNoteRequestedMessage());

        var expectedAbsolute = Path.Combine(_workspace, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains(expectedAbsolute, _fileService.FilesByPath);
        Assert.Equal(expectedRelativePath, sut.SelectedNode?.RelativePath);
    }

    private sealed class StubWorkspaceScanner : IWorkspaceScanner
    {
        public List<string> Paths { get; set; } = new();
        public string? LastRoot { get; private set; }

        public IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory)
        {
            LastRoot = rootDirectory;
            return Paths;
        }
    }

    private sealed class StubNoteDeleter : INoteDeleter
    {
        public List<string> DeletedPaths { get; } = new();
        public void Delete(string absolutePath) => DeletedPaths.Add(absolutePath);
    }

    private sealed class StubConfirmDialogService : IConfirmDialogService
    {
        public bool Result { get; set; } = true;
        public Task<bool> Confirm(string title, string message) => Task.FromResult(Result);
    }

    private sealed class StubNewNoteDialogService : INewNoteDialogService
    {
        public string? Response { get; set; }
        public string? LastParentDisplay { get; private set; }

        public Task<string?> PromptForName(string parentFolderDisplay, Func<string, string?> validate)
        {
            LastParentDisplay = parentFolderDisplay;
            return Task.FromResult(Response);
        }
    }

}
