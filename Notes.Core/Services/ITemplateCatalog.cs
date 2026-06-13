using System.Collections.Generic;
using Notes.Core.Models;

namespace Notes.Core.Services;

/// <summary>
/// Holds the templates under the workspace's top-level <c>.templates/</c> folder (flat — no
/// nested subfolders). <see cref="Load"/> refreshes the cached set from a workspace scan and is
/// driven by the tree reload (which runs on workspace change / create / delete — never on
/// autosave), so <see cref="List"/> and <see cref="HasAny"/> are cheap cache reads.
/// </summary>
public interface ITemplateCatalog
{
    void Load(IReadOnlyList<string> markdownRelativePaths);

    IReadOnlyList<TemplateInfo> List();

    bool HasAny();
}
