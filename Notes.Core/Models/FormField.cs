using System.Collections.Generic;

namespace Notes.Core.Models;

/// <summary>
/// One typed field declared under a template's <c>form</c> frontmatter key.
/// <see cref="Entries"/> is populated only for dropdown fields; <see cref="Format"/>
/// is an optional .NET format string interpreted per <see cref="Type"/> (a date
/// format for <c>date</c>, a numeric format for <c>number</c>; ignored otherwise).
/// </summary>
public sealed record FormField(
    string Type,
    string Label,
    IReadOnlyList<string>? Entries = null,
    string? Format = null);
