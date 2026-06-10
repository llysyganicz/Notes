using System.Threading;
using System.Threading.Tasks;

namespace Notes.Core.Services;

public interface INoteFileService
{
    string Read(string absolutePath);
    Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default);
    void Save(string absolutePath, string text);
}
