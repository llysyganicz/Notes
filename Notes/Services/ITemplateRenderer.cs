using System.Collections.Generic;
using Notes.Models;

namespace Notes.Services;

/// <summary>
/// Produces a generated note from a template plus collected field values: strips the
/// <c>form</c> block from the frontmatter and substitutes declared <c>{{field}}</c>
/// tokens in the body only.
/// </summary>
public interface ITemplateRenderer
{
    string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values);
}
