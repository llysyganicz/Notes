using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Notes.Messaging;
using Notes.Models;

namespace Notes.Services;

public sealed class NoteSearchIndex :
    INoteSearchIndex,
    IRecipient<WorkspaceChangedMessage>,
    IRecipient<NoteSavedMessage>,
    IRecipient<NoteDeletedMessage>
{
    private readonly IMessenger _messenger;
    private readonly IWorkspaceScanner _scanner;
    private readonly INoteFileService _fileService;
    private readonly INoteMetadataParser _parser;

    private readonly object _gate = new();
    private IReadOnlyDictionary<string, MetadataEntry> _entries = new Dictionary<string, MetadataEntry>();
    private readonly List<PendingMutation> _pendingDuringBuild = new();
    private string? _workspacePath;
    private bool _isReady;
    private CancellationTokenSource? _buildCts;

    public NoteSearchIndex(
        IMessenger messenger,
        IWorkspaceScanner scanner,
        INoteFileService fileService,
        INoteMetadataParser parser)
    {
        _messenger = messenger;
        _scanner = scanner;
        _fileService = fileService;
        _parser = parser;

        _messenger.RegisterAll(this);
    }

    public bool IsReady
    {
        get
        {
            lock (_gate) { return _isReady; }
        }
    }

    public void Receive(WorkspaceChangedMessage message)
    {
        CancellationTokenSource newCts = new();
        lock (_gate)
        {
            _buildCts?.Cancel();
            _buildCts = newCts;
            _pendingDuringBuild.Clear();
            _isReady = false;
            _workspacePath = message.WorkspacePath;
        }

        _messenger.Send(new SearchIndexStateChangedMessage(false));

        var workspacePath = message.WorkspacePath;
        _ = Task.Run(() => Build(workspacePath, newCts), newCts.Token);
    }

    public void Receive(NoteSavedMessage message)
    {
        var meta = _parser.Parse(message.Content);
        var entry = new MetadataEntry(Path.GetFileName(message.RelativePath), meta.Tags);

        lock (_gate)
        {
            var copy = new Dictionary<string, MetadataEntry>(_entries) { [message.RelativePath] = entry };
            _entries = copy;
            if (!_isReady)
            {
                _pendingDuringBuild.Add(new PendingMutation(message.RelativePath, entry));
            }
        }
    }

    public void Receive(NoteDeletedMessage message)
    {
        lock (_gate)
        {
            var copy = new Dictionary<string, MetadataEntry>(_entries);
            copy.Remove(message.RelativePath);
            _entries = copy;
            if (!_isReady)
            {
                _pendingDuringBuild.Add(new PendingMutation(message.RelativePath, null));
            }
        }
    }

    public async Task<IReadOnlyList<NoteSearchResult>> Search(
        string query,
        bool includeTemplates,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<NoteSearchResult>();
        }

        IReadOnlyDictionary<string, MetadataEntry> entries;
        string? workspace;
        bool isReady;
        lock (_gate)
        {
            entries = _entries;
            workspace = _workspacePath;
            isReady = _isReady;
        }

        if (!isReady || workspace is null)
        {
            return Array.Empty<NoteSearchResult>();
        }

        var tokens = query.ToLowerInvariant().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Array.Empty<NoteSearchResult>();
        }

        var ordered = entries
            .Where(kv => includeTemplates || !kv.Key.StartsWith(".templates/", StringComparison.Ordinal))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal);

        var results = new List<NoteSearchResult>();
        foreach (var kv in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = kv.Value;
            var unmatched = tokens.Where(t => !MatchesFilenameOrTags(entry, t)).ToList();

            if (unmatched.Count == 0)
            {
                results.Add(new NoteSearchResult(kv.Key, entry.FileName));
                continue;
            }

            var absolute = Path.Combine(workspace, kv.Key.Replace('/', Path.DirectorySeparatorChar));
            string body;
            try
            {
                body = await _fileService.ReadAsync(absolute, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            if (unmatched.All(t => body.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                results.Add(new NoteSearchResult(kv.Key, entry.FileName));
            }
        }

        return results;
    }

    private static bool MatchesFilenameOrTags(MetadataEntry entry, string token) =>
        entry.FileName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
        || entry.Tags.Any(tag => tag.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

    private async Task Build(string workspacePath, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var paths = _scanner.ScanMarkdownFiles(workspacePath);
            var newEntries = new Dictionary<string, MetadataEntry>(paths.Count);
            foreach (var relativePath in paths)
            {
                token.ThrowIfCancellationRequested();
                var absolute = Path.Combine(
                    workspacePath,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                string text;
                try
                {
                    text = await _fileService.ReadAsync(absolute, token).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    Trace.WriteLine($"Search index skipped '{absolute}' during build: {ex.Message}");
                    continue;
                }
                var meta = _parser.Parse(text);
                newEntries[relativePath] = new MetadataEntry(
                    Path.GetFileName(relativePath),
                    meta.Tags);
            }

            bool publish = false;
            lock (_gate)
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_buildCts, cts))
                {
                    return;
                }
                foreach (var op in _pendingDuringBuild)
                {
                    if (op.Entry is null)
                    {
                        newEntries.Remove(op.RelativePath);
                    }
                    else
                    {
                        newEntries[op.RelativePath] = op.Entry;
                    }
                }
                _pendingDuringBuild.Clear();
                _entries = newEntries;
                _isReady = true;
                publish = true;
            }

            if (publish)
            {
                _messenger.Send(new SearchIndexStateChangedMessage(true));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a newer WorkspaceChangedMessage; discard partial work silently.
        }
    }

    private sealed record MetadataEntry(string FileName, IReadOnlyList<string> Tags);

    private sealed record PendingMutation(string RelativePath, MetadataEntry? Entry);
}
