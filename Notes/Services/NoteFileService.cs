using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Notes.Services;

public sealed class NoteFileService : INoteFileService
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathGuard _pathGuard;

    public NoteFileService(IFileSystem fileSystem, IPathGuard pathGuard)
    {
        _fileSystem = fileSystem;
        _pathGuard = pathGuard;
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

    public const string TempSuffix = ".tmp";

    public void Save(string absolutePath, string text)
    {
        _pathGuard.EnsureWithinWorkspace(absolutePath);
        var dir = _fileSystem.Path.GetDirectoryName(absolutePath)!;
        var temp = _fileSystem.Path.Combine(dir, _fileSystem.Path.GetFileName(absolutePath) + TempSuffix);
        try
        {
            _fileSystem.File.WriteAllText(temp, text);
            _fileSystem.File.Move(temp, absolutePath, overwrite: true);
        }
        catch
        {
            try { _fileSystem.File.Delete(temp); } catch { /* don't mask the real error */ }
            throw;
        }
    }
}
