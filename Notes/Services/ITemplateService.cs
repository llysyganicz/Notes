using System.Threading.Tasks;

namespace Notes.Services;

/// <summary>
/// Shared orchestration for the pick → parse → collect → render template pipeline.
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Renders the full generated note (frontmatter + body) for the "New from Template" path.
    /// Returns <c>null</c> if the user cancels the picker or form.
    /// </summary>
    Task<string?> RenderForNewNote(string workspacePath);

    /// <summary>
    /// Renders only the template body for the "Insert from Template" path.
    /// Returns <c>null</c> if the user cancels the picker or form.
    /// </summary>
    Task<string?> RenderForInsert(string workspacePath);
}
