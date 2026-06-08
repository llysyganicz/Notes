using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;
using Notes.Tests.Fakes;
using Notes.ViewModels;
using NSubstitute;
using Xunit;

namespace Notes.Tests;

public sealed class NoteTreeViewModelTests
{
    private const string Workspace = "/workspace";

    private readonly MockFileSystem _fileSystem = new();
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly StubWorkspaceScanner _scanner = new();
    private readonly NoteTreeBuilder _treeBuilder;
    private readonly StubNoteDeleter _deleter = new();
    private readonly StubConfirmDialogService _confirm = new();
    private readonly NameValidator _nameValidator;
    private readonly INewNoteDialogService _newNoteDialog = Substitute.For<INewNoteDialogService>();
    private readonly InMemoryNoteFileService _fileService = new();
    private readonly NoteFolderService _folderService;
    private readonly ITemplateCatalog _templateCatalog = Substitute.For<ITemplateCatalog>();
    private readonly ITemplatePickerDialogService _templatePicker = Substitute.For<ITemplatePickerDialogService>();
    private readonly TemplateParser _templateParser = new();
    private readonly ITemplateFormDialogService _templateForm = Substitute.For<ITemplateFormDialogService>();
    private readonly TemplateRenderer _templateRenderer = new();

    public NoteTreeViewModelTests()
    {
        _treeBuilder = new NoteTreeBuilder(_fileSystem);
        _nameValidator = new NameValidator(_fileSystem);
        _folderService = new NoteFolderService(_fileSystem);
    }

    private NoteTreeViewModel BuildSut() =>
        new(_messenger, _scanner, _treeBuilder, _deleter, _confirm, _nameValidator, _newNoteDialog, _fileService, _folderService,
            _templateCatalog, _templatePicker, _templateParser, _templateForm, _templateRenderer);

    private void StubPrompt(string? response) =>
        _newNoteDialog.PromptForName(Arg.Any<string>(), Arg.Any<Func<string, string?>>())
            .Returns(Task.FromResult(response));

    [Fact]
    public void Receive_WhenWorkspaceChangedMessage_LoadsTree()
    {
        _scanner.Paths = new List<string> { "a.md", "sub/b.md" };
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        Assert.Equal(Workspace, _scanner.LastRoot);
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
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        NoteDeletedMessage? captured = null;
        _messenger.Register<NoteDeletedMessage>(this, (_, m) => captured = m);

        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        await sut.DeleteNoteCommand.ExecuteAsync(node);

        Assert.NotNull(captured);
        Assert.Equal("x.md", captured!.RelativePath);
        var expected = Path.Combine(Workspace, "x.md");
        Assert.Contains(expected, _deleter.DeletedPaths);
    }

    [Fact]
    public async Task DeleteNote_WhenConfirmed_RefreshesTree()
    {
        _scanner.Paths = new List<string> { "x.md", "y.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        _scanner.Paths = new List<string> { "y.md" };
        var node = new NoteTreeNode("x.md", "x.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        await sut.DeleteNoteCommand.ExecuteAsync(node);

        Assert.NotNull(sut.Root);
        var child = Assert.Single(sut.Root!.Children);
        Assert.Equal("y.md", child.Name);
    }

    [Fact]
    public async Task DeleteNote_WhenFolderConfirmed_DeletesFolderAndSendsMessagePerDescendantFile()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        var deleted = new List<NoteDeletedMessage>();
        _messenger.Register<NoteDeletedMessage>(this, (_, m) => deleted.Add(m));

        var fileA = new NoteTreeNode("a.md", "docs/a.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        var fileB = new NoteTreeNode("b.md", "docs/b.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        var nestedFile = new NoteTreeNode("c.md", "docs/sub/c.md", NoteNodeKind.File, Array.Empty<NoteTreeNode>());
        var subFolder = new NoteTreeNode("sub", "docs/sub", NoteNodeKind.Folder, new[] { nestedFile });
        var folder = new NoteTreeNode("docs", "docs", NoteNodeKind.Folder, new[] { fileA, fileB, subFolder });

        await sut.DeleteNoteCommand.ExecuteAsync(folder);

        Assert.Contains(Path.Combine(Workspace, "docs"), _deleter.DeletedFolders);
        Assert.Equal(
            new[] { "docs/a.md", "docs/b.md", "docs/sub/c.md" },
            deleted.Select(m => m.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DeleteNote_WhenRootFolder_CannotExecute()
    {
        var sut = BuildSut();

        var root = new NoteTreeNode(string.Empty, string.Empty, NoteNodeKind.Folder, Array.Empty<NoteTreeNode>());
        var folder = new NoteTreeNode("docs", "docs", NoteNodeKind.Folder, Array.Empty<NoteTreeNode>());

        Assert.False(sut.DeleteNoteCommand.CanExecute(root));
        Assert.True(sut.DeleteNoteCommand.CanExecute(folder));
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
        _scanner.Paths = selection == NewNoteSelection.NoSelection
            ? new List<string>()
            : new List<string> { "sub/x.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        sut.SelectedNode = selection switch
        {
            NewNoteSelection.NoSelection => null,
            NewNoteSelection.FolderSelected => new NoteTreeNode("sub", "sub", NoteNodeKind.Folder, Array.Empty<NoteTreeNode>()),
            NewNoteSelection.FileSelected => new NoteTreeNode("x.md", selectedRelativePath, NoteNodeKind.File, Array.Empty<NoteTreeNode>()),
            _ => null,
        };

        StubPrompt("untitled");
        _scanner.Paths = selection == NewNoteSelection.NoSelection
            ? new List<string> { "untitled.md" }
            : new List<string> { "sub/x.md", "sub/untitled.md" };

        _messenger.Send(new NewNoteRequestedMessage());

        var expectedAbsolute = Path.Combine(Workspace, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains(expectedAbsolute, _fileService.FilesByPath);
        Assert.Equal(expectedRelativePath, sut.SelectedNode?.RelativePath);
    }

    [Fact]
    public void Receive_WhenNewNoteRequestedMessageWithoutWorkspace_DoesNothing()
    {
        var sut = BuildSut();
        StubPrompt("untitled");

        _messenger.Send(new NewNoteRequestedMessage());

        Assert.Empty(_fileService.FilesByPath);
    }

    [Fact]
    public void Receive_WhenNewNoteDialogCancelled_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        StubPrompt(null);

        _messenger.Send(new NewNoteRequestedMessage());

        Assert.Empty(_fileService.FilesByPath);
    }

    [Fact]
    public void Receive_WhenNewNoteDefensiveValidationFails_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        StubPrompt("bad/name");

        _messenger.Send(new NewNoteRequestedMessage());

        Assert.Empty(_fileService.FilesByPath);
    }

    [Theory]
    [InlineData(NewNoteSelection.NoSelection, "", "ideas")]
    [InlineData(NewNoteSelection.FolderSelected, "sub", "sub/ideas")]
    [InlineData(NewNoteSelection.FileSelected, "sub/x.md", "sub/ideas")]
    public void Receive_WhenNewFolderRequestedMessage_CreatesFolderAtResolvedParentAndSelectsIt(
        NewNoteSelection selection,
        string selectedRelativePath,
        string expectedRelativePath)
    {
        _scanner.Paths = selection == NewNoteSelection.NoSelection
            ? new List<string>()
            : new List<string> { "sub/x.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        sut.SelectedNode = selection switch
        {
            NewNoteSelection.NoSelection => null,
            NewNoteSelection.FolderSelected => new NoteTreeNode("sub", "sub", NoteNodeKind.Folder, Array.Empty<NoteTreeNode>()),
            NewNoteSelection.FileSelected => new NoteTreeNode("x.md", selectedRelativePath, NoteNodeKind.File, Array.Empty<NoteTreeNode>()),
            _ => null,
        };

        StubPrompt("ideas");

        _messenger.Send(new NewFolderRequestedMessage());

        var expectedAbsolute = Path.Combine(Workspace, expectedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(_fileSystem.Directory.Exists(expectedAbsolute));
        Assert.Equal(expectedRelativePath, sut.SelectedNode?.RelativePath);
    }

    [Fact]
    public async Task NewFolderCommand_WhenFolderNodePassed_CreatesChildFolder()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        StubPrompt("child");

        var node = new NoteTreeNode("parent", "parent", NoteNodeKind.Folder, Array.Empty<NoteTreeNode>());
        await sut.NewFolderCommand.ExecuteAsync(node);

        Assert.True(_fileSystem.Directory.Exists(Path.Combine(Workspace, "parent", "child")));
    }

    [Fact]
    public void Receive_WhenNewFolderRequestedWithoutWorkspace_DoesNothing()
    {
        var sut = BuildSut();
        StubPrompt("ideas");

        _messenger.Send(new NewFolderRequestedMessage());

        Assert.False(_fileSystem.Directory.Exists(Path.Combine(Workspace, "ideas")));
    }

    [Fact]
    public void Receive_WhenNewFolderDialogCancelled_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        StubPrompt(null);

        _messenger.Send(new NewFolderRequestedMessage());

        Assert.False(_fileSystem.Directory.Exists(Workspace));
    }

    [Fact]
    public void Receive_WhenNewFolderDefensiveValidationFails_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        StubPrompt("bad/name");

        _messenger.Send(new NewFolderRequestedMessage());

        Assert.False(_fileSystem.Directory.Exists(Workspace));
    }

    [Fact]
    public void Receive_WhenNewFromTemplateRequested_RendersTemplateAndSavesNote()
    {
        const string templateText = "---\nform:\n  topic:\n    type: text\n    label: Topic\nkeep: yes\n---\n# {{topic}}\n";
        _scanner.Paths = new List<string> { ".templates/meeting.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        var templateInfo = new TemplateInfo(".templates/meeting.md", "meeting.md");
        _templateCatalog.List().Returns(new[] { templateInfo });
        _templatePicker.PickTemplate(Arg.Any<IReadOnlyList<TemplateInfo>>())
            .Returns(Task.FromResult<TemplateInfo?>(templateInfo));
        _fileService.FilesByPath[Path.Combine(Workspace, ".templates", "meeting.md")] = templateText;
        _templateForm.CollectValues(Arg.Any<FormDefinition>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>?>(
                new Dictionary<string, string> { ["topic"] = "Standup" }));
        StubPrompt("notes");
        _scanner.Paths = new List<string> { ".templates/meeting.md", "notes.md" };

        _messenger.Send(new NewFromTemplateRequestedMessage());

        var savedPath = Path.Combine(Workspace, "notes.md");
        Assert.True(_fileService.FilesByPath.ContainsKey(savedPath));
        var saved = _fileService.FilesByPath[savedPath];
        Assert.Contains("# Standup", saved);
        Assert.Contains("keep: yes", saved);
        Assert.DoesNotContain("form:", saved);
    }

    [Fact]
    public void Receive_WhenTemplatePickerCancelled_CreatesNoNote()
    {
        _scanner.Paths = new List<string> { ".templates/meeting.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        var templateInfo = new TemplateInfo(".templates/meeting.md", "meeting.md");
        _templateCatalog.List().Returns(new[] { templateInfo });
        _templatePicker.PickTemplate(Arg.Any<IReadOnlyList<TemplateInfo>>())
            .Returns(Task.FromResult<TemplateInfo?>(null));
        StubPrompt("notes");

        _messenger.Send(new NewFromTemplateRequestedMessage());

        Assert.False(_fileService.FilesByPath.ContainsKey(Path.Combine(Workspace, "notes.md")));
    }

    [Fact]
    public void Receive_WhenNoTemplatesAvailable_CreatesNoNote()
    {
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        _templateCatalog.List().Returns(Array.Empty<TemplateInfo>());
        StubPrompt("notes");

        _messenger.Send(new NewFromTemplateRequestedMessage());

        Assert.Empty(_fileService.FilesByPath);
    }

    [Fact]
    public void Receive_WhenMalformedTemplate_SkipsFormDialogAndSavesStaticBody()
    {
        // Tab-indented form: → TemplateParser returns FormDefinition.Empty (0 fields) → no form dialog.
        const string templateText = "---\nform:\n\tfield1:\n\t\ttype: text\n---\n# Static Body\n";
        _scanner.Paths = new List<string> { ".templates/broken.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        var templateInfo = new TemplateInfo(".templates/broken.md", "broken.md");
        _templateCatalog.List().Returns(new[] { templateInfo });
        _templatePicker.PickTemplate(Arg.Any<IReadOnlyList<TemplateInfo>>())
            .Returns(Task.FromResult<TemplateInfo?>(templateInfo));
        _fileService.FilesByPath[Path.Combine(Workspace, ".templates", "broken.md")] = templateText;
        StubPrompt("broken-note");
        _scanner.Paths = new List<string> { ".templates/broken.md", "broken-note.md" };

        _messenger.Send(new NewFromTemplateRequestedMessage());

        _templateForm.DidNotReceive().CollectValues(Arg.Any<FormDefinition>());
        var savedPath = Path.Combine(Workspace, "broken-note.md");
        Assert.True(_fileService.FilesByPath.ContainsKey(savedPath));
        Assert.Equal("# Static Body\n", _fileService.FilesByPath[savedPath]);
    }

    [Fact]
    public void Receive_WhenNewNoteNameCollidesWithExisting_DoesNotOverwriteOriginal()
    {
        // Fixed oracle — not derived from the renderer or from the dialog input
        const string OriginalContent = "# Original Note\n\nThis content must survive a collision.\n";
        var collidingPath = Path.Combine(Workspace, "existing.md");

        // Pre-seed MockFileSystem so NameValidator.ValidateNoteName sees the file exists
        _fileSystem.AddFile(collidingPath, new MockFileData(OriginalContent));
        // Mirror in the service fake as the stable assertion source
        _fileService.FilesByPath[collidingPath] = OriginalContent;

        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        NoteSavedMessage? saved = null;
        _messenger.Register<NoteSavedMessage>(this, (_, m) => saved = m);

        // Dialog returns the colliding name; the guard at NoteTreeViewModel:200 must block the write
        StubPrompt("existing");
        _messenger.Send(new NewNoteRequestedMessage());

        Assert.Equal(OriginalContent, _fileService.FilesByPath[collidingPath]);
        Assert.Null(saved); // guard short-circuited before any save was published
    }

    [Fact]
    public void Receive_WhenFormSubmittedBlank_SavesNoteWithNoLeftoverDeclaredTokens()
    {
        const string templateText = "---\nform:\n  topic:\n    type: text\n    label: Topic\n---\n# {{topic}}\n";
        _scanner.Paths = new List<string> { ".templates/meeting.md" };
        var sut = BuildSut();
        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        var templateInfo = new TemplateInfo(".templates/meeting.md", "meeting.md");
        _templateCatalog.List().Returns(new[] { templateInfo });
        _templatePicker.PickTemplate(Arg.Any<IReadOnlyList<TemplateInfo>>())
            .Returns(Task.FromResult<TemplateInfo?>(templateInfo));
        _fileService.FilesByPath[Path.Combine(Workspace, ".templates", "meeting.md")] = templateText;
        _templateForm.CollectValues(Arg.Any<FormDefinition>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>?>(
                new Dictionary<string, string>()));
        StubPrompt("blank-note");
        _scanner.Paths = new List<string> { ".templates/meeting.md", "blank-note.md" };

        _messenger.Send(new NewFromTemplateRequestedMessage());

        var savedPath = Path.Combine(Workspace, "blank-note.md");
        Assert.True(_fileService.FilesByPath.ContainsKey(savedPath));
        var saved = _fileService.FilesByPath[savedPath];
        Assert.DoesNotContain("{{topic}}", saved);
        Assert.Equal("# \n", saved);
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
        public List<string> DeletedFolders { get; } = new();
        public void Delete(string absolutePath) => DeletedPaths.Add(absolutePath);
        public void DeleteFolder(string absolutePath) => DeletedFolders.Add(absolutePath);
    }

    private sealed class StubConfirmDialogService : IConfirmDialogService
    {
        public bool Result { get; set; } = true;
        public Task<bool> Confirm(string title, string message) => Task.FromResult(Result);
    }
}
