using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Notes.Core.Models;

namespace Notes.Core.Services;

public interface INoteSearchIndex
{
    bool IsReady { get; }

    Task<IReadOnlyList<NoteSearchResult>> Search(
        string query,
        bool includeTemplates,
        CancellationToken cancellationToken = default);
}
