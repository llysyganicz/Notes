using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Models;

/// <summary>One declared field paired with its name (the placeholder key).</summary>
public sealed record FormFieldEntry(string Name, FormField Field);

/// <summary>
/// The parsed <c>form</c> schema for a template: an ordered list of fields preserving
/// template document order, plus a convenience set of declared field names the renderer
/// uses to decide which <c>{{token}}</c>s to substitute.
/// </summary>
public sealed record FormDefinition(IReadOnlyList<FormFieldEntry> Fields)
{
    public static FormDefinition Empty { get; } = new(Array.Empty<FormFieldEntry>());

    public IReadOnlySet<string> Names { get; } =
        Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
}
