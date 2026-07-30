using System.Collections.Generic;
using Notes.Core.Models;

namespace Notes.Core.Services;

/// <summary>
/// Produces a generated note from a template plus collected field values: strips the
/// <c>form</c> block from the frontmatter and substitutes declared <c>{{field}}</c>
/// tokens in the body only.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Renders the full generated note from the template, keeping non-form frontmatter
    /// and substituting declared tokens in the body.
    /// </summary>
    string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values);

    /// <summary>
    /// Renders only the body region of the template, omitting the frontmatter block entirely.
    /// </summary>
    string RenderBody(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values);
}
