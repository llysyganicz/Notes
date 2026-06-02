using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;
using Notes.Services;

namespace Notes.ViewModels;

public sealed partial class NoteSearchViewModel :
    ObservableObject,
    IRecipient<OpenSearchRequestedMessage>,
    IRecipient<WorkspaceChangedMessage>,
    IRecipient<SearchIndexStateChangedMessage>
{
    private readonly IMessenger _messenger;
    private readonly INoteSearchIndex _index;
    private readonly DispatcherTimer _debounceTimer;

    private CancellationTokenSource? _searchCts;
    private bool _suppressChangeHandlers;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _includeTemplates;

    [ObservableProperty]
    private bool _isIndexReady;

    [ObservableProperty]
    private IReadOnlyList<NoteSearchResult> _results = Array.Empty<NoteSearchResult>();

    [ObservableProperty]
    private NoteSearchResult? _selectedResult;

    public NoteSearchViewModel(IMessenger messenger, INoteSearchIndex index)
        : this(messenger, index, TimeSpan.FromMilliseconds(150))
    {
    }

    public NoteSearchViewModel(IMessenger messenger, INoteSearchIndex index, TimeSpan debounceInterval)
    {
        _messenger = messenger;
        _index = index;
        _isIndexReady = _index.IsReady;

        _debounceTimer = new DispatcherTimer { Interval = debounceInterval };
        _debounceTimer.Tick += OnDebounceTick;

        _messenger.RegisterAll(this);
    }

    partial void OnQueryChanged(string value)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    partial void OnIncludeTemplatesChanged(bool value)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        TriggerSearchNow();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        TriggerSearchNow();
    }

    private void TriggerSearchNow()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = RunSearch(cts.Token);
    }

    private async Task RunSearch(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            Results = Array.Empty<NoteSearchResult>();
            return;
        }

        try
        {
            var hits = await _index.Search(Query, IncludeTemplates, token);
            if (!token.IsCancellationRequested)
            {
                Results = hits;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query; ignore.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenResult(NoteSearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        var node = new NoteTreeNode(
            result.FileName,
            result.RelativePath,
            NoteNodeKind.File,
            Array.Empty<NoteTreeNode>());
        _messenger.Send(new NoteSelectedMessage(node));
        Close();
    }

    [RelayCommand]
    private void Close()
    {
        _debounceTimer.Stop();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        _suppressChangeHandlers = true;
        try
        {
            IsOpen = false;
            Query = string.Empty;
            IncludeTemplates = false;
            Results = Array.Empty<NoteSearchResult>();
            SelectedResult = null;
        }
        finally
        {
            _suppressChangeHandlers = false;
        }
    }

    public void Receive(OpenSearchRequestedMessage message)
    {
        IsOpen = true;
    }

    public void Receive(WorkspaceChangedMessage message)
    {
        Close();
    }

    public void Receive(SearchIndexStateChangedMessage message)
    {
        IsIndexReady = message.IsReady;
        if (message.IsReady && IsOpen && !string.IsNullOrWhiteSpace(Query))
        {
            TriggerSearchNow();
        }
    }
}
