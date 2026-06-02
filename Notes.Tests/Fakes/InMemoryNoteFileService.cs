using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Notes.Services;

namespace Notes.Tests.Fakes;

public sealed class InMemoryNoteFileService : INoteFileService
{
    public Dictionary<string, string> FilesByPath { get; } = new();

    public string Read(string absolutePath) =>
        FilesByPath.TryGetValue(absolutePath, out var value) ? value : string.Empty;

    public Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(absolutePath));

    public void Save(string absolutePath, string text) =>
        FilesByPath[absolutePath] = text;
}
