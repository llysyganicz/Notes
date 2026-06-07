using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Notes.Services;

public sealed class NoteFileService : INoteFileService
{
    private readonly IFileSystem _fileSystem;

    public NoteFileService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public string Read(string absolutePath)
    {
        if (!_fileSystem.File.Exists(absolutePath))
        {
            return string.Empty;
        }

        return _fileSystem.File.ReadAllText(absolutePath);
    }

    public Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.File.Exists(absolutePath))
        {
            return Task.FromResult(string.Empty);
        }

        return _fileSystem.File.ReadAllTextAsync(absolutePath, cancellationToken);
    }

    public void Save(string absolutePath, string text)
    {
        _fileSystem.File.WriteAllText(absolutePath, text);
    }
}
