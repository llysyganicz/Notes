using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;
using Notes.ViewModels;
using Xunit;

namespace Notes.Tests;

public sealed class NoteSearchViewModelTests
{
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly StubNoteSearchIndex _index = new();

    private NoteSearchViewModel BuildSut(TimeSpan? debounce = null) =>
        new(_messenger, _index, debounce ?? TimeSpan.Zero);

    [AvaloniaFact]
    public void Construct_WhenIndexAlreadyReady_MirrorsIsIndexReadyTrue()
    {
        _index.IsReady = true;

        var sut = BuildSut();

        Assert.True(sut.IsIndexReady);
    }

    [AvaloniaFact]
    public void Construct_WhenIndexNotReady_MirrorsIsIndexReadyFalse()
    {
        _index.IsReady = false;

        var sut = BuildSut();

        Assert.False(sut.IsIndexReady);
    }

    [AvaloniaFact]
    public void Receive_WhenOpenSearchRequestedMessage_SetsIsOpenTrue()
    {
        var sut = BuildSut();

        _messenger.Send(new OpenSearchRequestedMessage());

        Assert.True(sut.IsOpen);
    }

    [AvaloniaFact]
    public void Receive_WhenWorkspaceChangedMessageWhileOpen_ClosesAndClearsState()
    {
        var sut = BuildSut();
        _messenger.Send(new OpenSearchRequestedMessage());
        sut.Query = "hello";
        sut.IncludeTemplates = true;

        _messenger.Send(new WorkspaceChangedMessage("/workspace"));

        Assert.False(sut.IsOpen);
        Assert.Equal(string.Empty, sut.Query);
        Assert.False(sut.IncludeTemplates);
        Assert.Empty(sut.Results);
        Assert.Null(sut.SelectedResult);
    }

    [AvaloniaFact]
    public async Task Receive_WhenSearchIndexStateChangedTrueAndOverlayOpenWithQuery_TriggersSearch()
    {
        _index.IsReady = false;
        _index.NextResults = new[] { new NoteSearchResult("note.md", "note.md") };
        var sut = BuildSut();
        _messenger.Send(new OpenSearchRequestedMessage());
        sut.Query = "note";
        await AwaitNextResults(sut);
        _index.SearchCalls.Clear();

        _index.IsReady = true;
        _messenger.Send(new SearchIndexStateChangedMessage(true));
        await _index.WaitForSearchStart();

        Assert.True(sut.IsIndexReady);
        Assert.NotEmpty(_index.SearchCalls);
    }

    [AvaloniaFact]
    public void Receive_WhenSearchIndexStateChangedFalse_SetsIsIndexReadyFalse()
    {
        _index.IsReady = true;
        var sut = BuildSut();

        _messenger.Send(new SearchIndexStateChangedMessage(false));

        Assert.False(sut.IsIndexReady);
    }

    [AvaloniaFact]
    public async Task OnQueryChanged_WhenCalled_TriggersSearchAfterDebounce()
    {
        _index.NextResults = new[] { new NoteSearchResult("a.md", "a.md") };
        var sut = BuildSut();

        var resultsChanged = AwaitNextResults(sut);
        sut.Query = "a";
        await resultsChanged;

        Assert.Single(sut.Results);
        Assert.Single(_index.SearchCalls);
        Assert.Equal("a", _index.SearchCalls[0].Query);
    }

    [AvaloniaFact]
    public async Task OnQueryChanged_WhenSetToEmpty_ClearsResultsWithoutCallingIndex()
    {
        _index.NextResults = new[] { new NoteSearchResult("a.md", "a.md") };
        var sut = BuildSut();
        var firstSearch = AwaitNextResults(sut);
        sut.Query = "a";
        await firstSearch;
        _index.SearchCalls.Clear();

        var cleared = AwaitNextResults(sut);
        sut.Query = string.Empty;
        await cleared;

        Assert.Empty(sut.Results);
        Assert.Empty(_index.SearchCalls);
    }

    [AvaloniaFact]
    public async Task OnQueryChanged_WhenCalledTwiceRapidly_CancelsFirstSearchBeforeStartingSecond()
    {
        _index.HoldSearch = true;
        var sut = BuildSut();

        sut.Query = "first";
        await _index.WaitForSearchStart();
        var firstCall = _index.SearchCalls[^1];

        sut.Query = "second";
        await _index.WaitForSearchStart(expected: 2);

        Assert.True(firstCall.Token.IsCancellationRequested);
    }

    [AvaloniaFact]
    public async Task OnIncludeTemplatesChanged_WhenToggled_TriggersSearchImmediately()
    {
        _index.NextResults = new[] { new NoteSearchResult("a.md", "a.md") };
        var sut = BuildSut();
        var firstSearch = AwaitNextResults(sut);
        sut.Query = "a";
        await firstSearch;
        _index.SearchCalls.Clear();

        sut.IncludeTemplates = true;
        await _index.WaitForSearchStart();

        Assert.Single(_index.SearchCalls);
        Assert.True(_index.SearchCalls[0].IncludeTemplates);
    }

    [AvaloniaFact]
    public void OpenResult_WhenCalledWithResult_PublishesNoteSelectedMessageAndClosesOverlay()
    {
        var sut = BuildSut();
        _messenger.Send(new OpenSearchRequestedMessage());
        NoteSelectedMessage? captured = null;
        _messenger.Register<NoteSelectedMessage>(this, (_, m) => captured = m);

        sut.OpenResultCommand.Execute(new NoteSearchResult("sub/note.md", "note.md"));

        Assert.NotNull(captured);
        Assert.Equal("sub/note.md", captured!.Node?.RelativePath);
        Assert.Equal("note.md", captured.Node?.Name);
        Assert.Equal(NoteNodeKind.File, captured.Node?.Kind);
        Assert.False(sut.IsOpen);
    }

    [AvaloniaFact]
    public void OpenResult_WhenCalledWithNull_DoesNothing()
    {
        var sut = BuildSut();
        _messenger.Send(new OpenSearchRequestedMessage());
        NoteSelectedMessage? captured = null;
        _messenger.Register<NoteSelectedMessage>(this, (_, m) => captured = m);

        sut.OpenResultCommand.Execute(null);

        Assert.Null(captured);
        Assert.True(sut.IsOpen);
    }

    [AvaloniaFact]
    public async Task Close_WhenCalled_ResetsQueryAndIncludeTemplatesAndResultsAndSelectedResult()
    {
        _index.NextResults = new[] { new NoteSearchResult("a.md", "a.md") };
        var sut = BuildSut();
        _messenger.Send(new OpenSearchRequestedMessage());
        var firstSearch = AwaitNextResults(sut);
        sut.Query = "a";
        await firstSearch;
        sut.IncludeTemplates = true;
        sut.SelectedResult = sut.Results[0];

        sut.CloseCommand.Execute(null);

        Assert.False(sut.IsOpen);
        Assert.Equal(string.Empty, sut.Query);
        Assert.False(sut.IncludeTemplates);
        Assert.Empty(sut.Results);
        Assert.Null(sut.SelectedResult);
    }

    [AvaloniaFact]
    public async Task Close_WhenCalledWithSearchInFlight_CancelsTheSearch()
    {
        _index.HoldSearch = true;
        var sut = BuildSut();

        sut.Query = "anything";
        await _index.WaitForSearchStart();
        var call = _index.SearchCalls[^1];

        sut.CloseCommand.Execute(null);

        Assert.True(call.Token.IsCancellationRequested);
    }

    [AvaloniaFact]
    public async Task Search_WhenCalledFromTimer_PassesCurrentQueryAndIncludeTemplatesToIndex()
    {
        _index.NextResults = new[] { new NoteSearchResult("hit.md", "hit.md") };
        var sut = BuildSut();
        sut.IncludeTemplates = true;
        // Toggling IncludeTemplates triggers an immediate empty-query search;
        // ignore that call before asserting the next one.
        _index.SearchCalls.Clear();

        var nextSearch = AwaitNextResults(sut);
        sut.Query = "hello";
        await nextSearch;

        Assert.Single(_index.SearchCalls);
        Assert.Equal("hello", _index.SearchCalls[0].Query);
        Assert.True(_index.SearchCalls[0].IncludeTemplates);
    }

    [AvaloniaFact]
    public async Task Search_WhenIndexThrowsOperationCanceled_DoesNotSurfaceAsError()
    {
        _index.ThrowOnSearch = new OperationCanceledException();
        var sut = BuildSut();

        sut.Query = "x";
        await _index.WaitForSearchStart();

        // No exception leaks; results remain at their initial value.
        Assert.Empty(sut.Results);
    }

    private static Task AwaitNextResults(NoteSearchViewModel vm)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Results))
            {
                vm.PropertyChanged -= handler;
                tcs.TrySetResult();
            }
        };
        vm.PropertyChanged += handler;
        return tcs.Task;
    }

    private sealed class StubNoteSearchIndex : INoteSearchIndex
    {
        public bool IsReady { get; set; }
        public IReadOnlyList<NoteSearchResult> NextResults { get; set; } = Array.Empty<NoteSearchResult>();
        public List<SearchCall> SearchCalls { get; } = new();
        public bool HoldSearch { get; set; }
        public Exception? ThrowOnSearch { get; set; }

        private readonly object _gate = new();
        private TaskCompletionSource _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<NoteSearchResult>> Search(
            string query,
            bool includeTemplates,
            CancellationToken cancellationToken = default)
        {
            SearchCalls.Add(new SearchCall(query, includeTemplates, cancellationToken));
            lock (_gate)
            {
                _startSignal.TrySetResult();
                _startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (ThrowOnSearch is not null)
            {
                return Task.FromException<IReadOnlyList<NoteSearchResult>>(ThrowOnSearch);
            }

            if (HoldSearch)
            {
                return WaitForCancellation(cancellationToken);
            }

            return Task.FromResult(NextResults);
        }

        public Task WaitForSearchStart(int expected = 1)
        {
            // Already at the expected count: synchronous fast path.
            if (SearchCalls.Count >= expected)
            {
                return Task.CompletedTask;
            }

            Task task;
            lock (_gate) { task = _startSignal.Task; }
            return task;
        }

        private static async Task<IReadOnlyList<NoteSearchResult>> WaitForCancellation(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<NoteSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = token.Register(() => tcs.TrySetCanceled(token));
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private sealed record SearchCall(string Query, bool IncludeTemplates, CancellationToken Token);
}
