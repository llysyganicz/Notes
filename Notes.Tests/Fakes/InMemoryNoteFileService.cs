using System.Collections.Generic;
using Notes.Services;

namespace Notes.Tests.Fakes;

public sealed class InMemoryNoteFileService : INoteFileService
{
    public Dictionary<string, string> FilesByPath { get; } = new();

    public string Read(string absolutePath) =>
        FilesByPath.TryGetValue(absolutePath, out var value) ? value : string.Empty;

    public void Save(string absolutePath, string text) =>
        FilesByPath[absolutePath] = text;
}
