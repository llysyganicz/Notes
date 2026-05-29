using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;
using Notes.ViewModels;
using Xunit;

namespace Notes.Tests;

public sealed class NoteTreeViewModelTests
{
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly StubWorkspaceScanner _scanner = new();
    private readonly NoteTreeBuilder _treeBuilder = new();
    private readonly StubNoteDeleter _deleter = new();
    private readonly StubConfirmDialogService _confirm = new();

    private NoteTreeViewModel BuildSut() =>
        new(_messenger, _scanner, _treeBuilder, _deleter, _confirm);

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

        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, System.Array.Empty<NoteTreeNode>());
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

        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, System.Array.Empty<NoteTreeNode>());
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

        // After delete, the scanner returns only y.md
        _scanner.Paths = new List<string> { "y.md" };
        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, System.Array.Empty<NoteTreeNode>());
        await sut.DeleteNoteCommand.ExecuteAsync(node);

        Assert.NotNull(sut.Root);
        var child = Assert.Single(sut.Root!.Children);
        Assert.Equal("y.md", child.Name);
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
}
