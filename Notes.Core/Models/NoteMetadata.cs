using System;
using System.Collections.Generic;

namespace Notes.Core.Models;

public sealed record NoteMetadata(IReadOnlyList<string> Tags)
{
    public static NoteMetadata Empty { get; } = new(Array.Empty<string>());
}
