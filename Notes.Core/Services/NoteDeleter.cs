using System.IO.Abstractions;

namespace Notes.Core.Services;

public sealed class NoteDeleter : INoteDeleter
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathGuard _pathGuard;

    public NoteDeleter(IFileSystem fileSystem, IPathGuard pathGuard)
    {
        _fileSystem = fileSystem;
        _pathGuard = pathGuard;
    }

    public void Delete(string absolutePath)
    {
        _pathGuard.EnsureWithinWorkspace(absolutePath);
        _fileSystem.File.Delete(absolutePath);
    }

    public void DeleteFolder(string absolutePath)
    {
        _pathGuard.EnsureWithinWorkspace(absolutePath);
        _fileSystem.Directory.Delete(absolutePath, recursive: true);
    }
}
