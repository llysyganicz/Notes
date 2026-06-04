using Notes.Models;

namespace Notes.Services;

/// <summary>
/// Extracts the <c>form</c> schema from a template's YAML frontmatter into an
/// order-preserving <see cref="FormDefinition"/>. Missing/malformed/absent
/// frontmatter yields <see cref="FormDefinition.Empty"/>.
/// </summary>
public interface ITemplateParser
{
    FormDefinition Parse(string? templateText);
}
