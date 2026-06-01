using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NoteSearchIndexTests
{
    private const string Workspace = "/workspace";

    private readonly StrongReferenceMessenger _messenger = new();
    private readonly StubWorkspaceScanner _scanner = new();
    private readonly StubNoteFileService _fileService = new();
    private readonly NoteMetadataParser _parser = new();

    private NoteSearchIndex BuildSut() =>
        new(_messenger, _scanner, _fileService, _parser);

    [Fact]
    public void Construct_WhenCreated_IsReadyIsFalse()
    {
        var sut = BuildSut();

        Assert.False(sut.IsReady);
    }

    [Fact]
    public void Receive_WhenWorkspaceChangedMessage_PublishesNotReadyImmediately()
    {
        var sut = BuildSut();
        SearchIndexStateChangedMessage? captured = null;
        _messenger.Register<SearchIndexStateChangedMessage>(this, (_, m) =>
        {
            captured ??= m;
        });

        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        Assert.NotNull(captured);
        Assert.False(captured!.IsReady);
    }

    [Fact]
    public async Task Receive_WhenWorkspaceChangedMessage_BuildsAsynchronouslyThenBecomesReady()
    {
        _scanner.Paths = new[] { "a.md" };
        _fileService.Files["/workspace/a.md"] = "body";
        var sut = BuildSut();
        var ready = AwaitReady(_messenger);

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await ready;

        Assert.True(sut.IsReady);
    }

    [Fact]
    public async Task Receive_WhenWorkspaceChangedMessageDuringBuild_CancelsFirstBuildAndStartsSecond()
    {
        _scanner.Paths = new[] { "a.md" };
        _fileService.Files["/workspace/a.md"] = "first";
        _fileService.BlockReadAsync = true;
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));

        await _fileService.WaitForReadAsyncCall();

        _scanner.Paths = new[] { "b.md" };
        _fileService.Files["/other/b.md"] = "second";
        _fileService.BlockReadAsync = false;
        var ready = AwaitReady(_messenger);

        _messenger.Send(new WorkspaceChangedMessage("/other"));

        _fileService.ReleaseBlockedReads();

        await ready;

        Assert.True(sut.IsReady);
        var hits = await sut.Search("b", includeTemplates: false);
        Assert.Single(hits);
        Assert.Equal("b.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Receive_WhenNoteSavedMessage_UpsertsEntryWithoutCallingReadAsync()
    {
        _scanner.Paths = Array.Empty<string>();
        var sut = BuildSut();
        var ready = AwaitReady(_messenger);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await ready;

        _fileService.ReadAsyncCounts.Clear();
        _messenger.Send(new NoteSavedMessage("note.md", "hello"));

        Assert.Equal(0, _fileService.GetReadAsyncCount("/workspace/note.md"));
        var hits = await sut.Search("note", includeTemplates: false);
        Assert.Single(hits);
        Assert.Equal("note.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Receive_WhenNoteSavedMessageForNewPath_AddsNewEntry()
    {
        _scanner.Paths = new[] { "existing.md" };
        _fileService.Files["/workspace/existing.md"] = "";
        var sut = BuildSut();
        var ready = AwaitReady(_messenger);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await ready;

        _messenger.Send(new NoteSavedMessage("brand-new.md", "body"));

        var hits = await sut.Search("brand-new", includeTemplates: false);
        Assert.Single(hits);
    }

    [Fact]
    public async Task Receive_WhenNoteDeletedMessage_RemovesEntry()
    {
        _scanner.Paths = new[] { "a.md", "b.md" };
        _fileService.Files["/workspace/a.md"] = "";
        _fileService.Files["/workspace/b.md"] = "";
        var sut = BuildSut();
        var ready = AwaitReady(_messenger);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await ready;

        _messenger.Send(new NoteDeletedMessage("a.md"));

        var hits = await sut.Search("md", includeTemplates: false);
        Assert.Single(hits);
        Assert.Equal("b.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Receive_WhenNoteSavedMessageArrivesDuringBuild_SurvivesSwapIntoNewDictionary()
    {
        _scanner.Paths = new[] { "scanned.md" };
        _fileService.Files["/workspace/scanned.md"] = "";
        _fileService.BlockReadAsync = true;
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await _fileService.WaitForReadAsyncCall();

        _messenger.Send(new NoteSavedMessage("interleaved.md", "body"));

        var ready = AwaitReady(_messenger);
        _fileService.BlockReadAsync = false;
        _fileService.ReleaseBlockedReads();
        await ready;

        var hits = await sut.Search("interleaved", includeTemplates: false);
        Assert.Single(hits);
        Assert.Equal("interleaved.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Receive_WhenNoteDeletedMessageArrivesDuringBuild_AbsentFromNewDictionaryAfterSwap()
    {
        _scanner.Paths = new[] { "doomed.md" };
        _fileService.Files["/workspace/doomed.md"] = "";
        _fileService.BlockReadAsync = true;
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await _fileService.WaitForReadAsyncCall();

        _messenger.Send(new NoteDeletedMessage("doomed.md"));

        var ready = AwaitReady(_messenger);
        _fileService.BlockReadAsync = false;
        _fileService.ReleaseBlockedReads();
        await ready;

        var hits = await sut.Search("doomed", includeTemplates: false);
        Assert.Empty(hits);
    }

    [Fact]
    public async Task Receive_WhenWorkspaceChangedMessageDuringBuild_PendingBufferFromPreviousBuildIsDiscarded()
    {
        _scanner.Paths = new[] { "a.md" };
        _fileService.Files["/workspace/a.md"] = "";
        _fileService.BlockReadAsync = true;
        var sut = BuildSut();

        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await _fileService.WaitForReadAsyncCall();

        _messenger.Send(new NoteSavedMessage("leaky.md", "body"));

        _scanner.Paths = new[] { "fresh.md" };
        _fileService.Files["/other/fresh.md"] = "";
        var ready = AwaitReady(_messenger);
        _fileService.BlockReadAsync = false;
        _messenger.Send(new WorkspaceChangedMessage("/other"));
        _fileService.ReleaseBlockedReads();

        await ready;

        var leakyHits = await sut.Search("leaky", includeTemplates: false);
        Assert.Empty(leakyHits);
        var freshHits = await sut.Search("fresh", includeTemplates: false);
        Assert.Single(freshHits);
    }

    [Fact]
    public async Task Search_WhenQueryIsEmpty_ReturnsEmpty()
    {
        var sut = await BuildReadyIndex(Array.Empty<string>());

        var hits = await sut.Search("", includeTemplates: false);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_WhenQueryIsWhitespace_ReturnsEmpty()
    {
        var sut = await BuildReadyIndex(Array.Empty<string>());

        var hits = await sut.Search("   ", includeTemplates: false);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_WhenIndexNotReady_ReturnsEmpty()
    {
        var sut = BuildSut();

        var hits = await sut.Search("anything", includeTemplates: false);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_WhenSingleTermMatchesFilename_ReturnsMatchWithoutReadingBody()
    {
        _scanner.Paths = new[] { "groceries.md" };
        _fileService.Files["/workspace/groceries.md"] = "irrelevant body content";
        var sut = await BuildReadyIndex();
        _fileService.ReadAsyncCounts.Clear();

        var hits = await sut.Search("groc", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal(0, _fileService.GetReadAsyncCount("/workspace/groceries.md"));
    }

    [Fact]
    public async Task Search_WhenSingleTermMatchesTag_ReturnsMatchWithoutReadingBody()
    {
        _scanner.Paths = new[] { "note.md" };
        _fileService.Files["/workspace/note.md"] = "---\ntags: [urgent]\n---\nbody";
        var sut = await BuildReadyIndex();
        _fileService.ReadAsyncCounts.Clear();

        var hits = await sut.Search("urgent", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal(0, _fileService.GetReadAsyncCount("/workspace/note.md"));
    }

    [Fact]
    public async Task Search_WhenSingleTermMatchesBodyOnly_ReadsBodyAndReturnsMatch()
    {
        _scanner.Paths = new[] { "note.md" };
        _fileService.Files["/workspace/note.md"] = "the magic happens here";
        var sut = await BuildReadyIndex();
        _fileService.ReadAsyncCounts.Clear();

        var hits = await sut.Search("magic", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal(1, _fileService.GetReadAsyncCount("/workspace/note.md"));
    }

    [Fact]
    public async Task Search_WhenMultipleTermsOneMatchesFilenameOneMatchesBody_ReadsBodyForUnmatchedToken()
    {
        _scanner.Paths = new[] { "groceries.md" };
        _fileService.Files["/workspace/groceries.md"] = "milk bread eggs";
        var sut = await BuildReadyIndex();
        _fileService.ReadAsyncCounts.Clear();

        var hits = await sut.Search("groc milk", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal(1, _fileService.GetReadAsyncCount("/workspace/groceries.md"));
    }

    [Fact]
    public async Task Search_WhenMultipleTerms_RequiresAllToMatch()
    {
        _scanner.Paths = new[] { "alpha.md", "beta.md" };
        _fileService.Files["/workspace/alpha.md"] = "shared text";
        _fileService.Files["/workspace/beta.md"] = "unique text";
        var sut = await BuildReadyIndex();

        var hits = await sut.Search("alpha shared", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal("alpha.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Search_WhenQueryIsMixedCase_MatchesCaseInsensitively()
    {
        _scanner.Paths = new[] { "Note.md" };
        _fileService.Files["/workspace/Note.md"] = "Some Body";
        var sut = await BuildReadyIndex();

        var hits = await sut.Search("NoTe", includeTemplates: false);

        Assert.Single(hits);
    }

    [Fact]
    public async Task Search_WhenTemplatePathPresent_ExcludesByDefault()
    {
        _scanner.Paths = new[] { ".templates/t.md", "regular.md" };
        _fileService.Files["/workspace/.templates/t.md"] = "";
        _fileService.Files["/workspace/regular.md"] = "";
        var sut = await BuildReadyIndex();

        var hits = await sut.Search("md", includeTemplates: false);

        Assert.Single(hits);
        Assert.Equal("regular.md", hits[0].RelativePath);
    }

    [Fact]
    public async Task Search_WhenIncludeTemplatesTrue_IncludesTemplatePaths()
    {
        _scanner.Paths = new[] { ".templates/t.md", "regular.md" };
        _fileService.Files["/workspace/.templates/t.md"] = "";
        _fileService.Files["/workspace/regular.md"] = "";
        var sut = await BuildReadyIndex();

        var hits = await sut.Search("md", includeTemplates: true);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task Search_WhenMultipleMatches_ResultsOrderedByRelativePath()
    {
        _scanner.Paths = new[] { "zebra.md", "alpha.md", "middle.md" };
        _fileService.Files["/workspace/zebra.md"] = "";
        _fileService.Files["/workspace/alpha.md"] = "";
        _fileService.Files["/workspace/middle.md"] = "";
        var sut = await BuildReadyIndex();

        var hits = await sut.Search("md", includeTemplates: false);

        Assert.Equal(new[] { "alpha.md", "middle.md", "zebra.md" }, hits.Select(h => h.RelativePath));
    }

    [Fact]
    public async Task Search_WhenFileMissingDuringLazyRead_SkipsEntryWithoutThrowing()
    {
        _scanner.Paths = new[] { "ghost.md" };
        _fileService.Files["/workspace/ghost.md"] = "during build only";
        var sut = await BuildReadyIndex();
        _fileService.Files.Remove("/workspace/ghost.md");
        _fileService.MissingPaths.Add("/workspace/ghost.md");

        var hits = await sut.Search("nonsense-token", includeTemplates: false);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_WhenCancellationRequestedMidScan_ThrowsOperationCanceled()
    {
        _scanner.Paths = new[] { "a.md", "b.md", "c.md" };
        foreach (var p in _scanner.Paths)
        {
            _fileService.Files[$"/workspace/{p}"] = "";
        }
        var sut = await BuildReadyIndex();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.Search("anything", includeTemplates: false, cts.Token));
    }

    private async Task<NoteSearchIndex> BuildReadyIndex(IReadOnlyList<string>? paths = null)
    {
        if (paths is not null)
        {
            _scanner.Paths = paths;
        }
        var sut = BuildSut();
        var ready = AwaitReady(_messenger);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
        await ready;
        return sut;
    }

    private static Task AwaitReady(IMessenger messenger)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = new object();
        messenger.Register<SearchIndexStateChangedMessage>(token, (_, msg) =>
        {
            if (msg.IsReady)
            {
                messenger.Unregister<SearchIndexStateChangedMessage>(token);
                tcs.TrySetResult();
            }
        });
        return tcs.Task;
    }

    private sealed class StubWorkspaceScanner : IWorkspaceScanner
    {
        public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory) => Paths;
    }

    private sealed class StubNoteFileService : INoteFileService
    {
        public Dictionary<string, string> Files { get; } = new();
        public HashSet<string> MissingPaths { get; } = new();
        public ConcurrentDictionary<string, int> ReadAsyncCounts { get; } = new();

        public bool BlockReadAsync { get; set; }

        private readonly object _gate = new();
        private TaskCompletionSource _gateTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _readSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Read(string absolutePath) =>
            Files.TryGetValue(absolutePath, out var v) ? v : string.Empty;

        public async Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default)
        {
            ReadAsyncCounts.AddOrUpdate(absolutePath, 1, (_, n) => n + 1);
            _readSignal.TrySetResult();

            if (BlockReadAsync)
            {
                Task waitTask;
                lock (_gate) { waitTask = _gateTcs.Task; }
                using var reg = cancellationToken.Register(() =>
                {
                    lock (_gate) { _gateTcs.TrySetCanceled(cancellationToken); }
                });
                await waitTask.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (MissingPaths.Contains(absolutePath))
            {
                throw new FileNotFoundException(null, absolutePath);
            }

            return Files.TryGetValue(absolutePath, out var v) ? v : string.Empty;
        }

        public void Save(string absolutePath, string text) => Files[absolutePath] = text;

        public Task WaitForReadAsyncCall() => _readSignal.Task;

        public void ReleaseBlockedReads()
        {
            lock (_gate)
            {
                _gateTcs.TrySetResult();
                _gateTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _readSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public int GetReadAsyncCount(string absolutePath) =>
            ReadAsyncCounts.TryGetValue(absolutePath, out var n) ? n : 0;
    }
}
