using System.IO.Abstractions;

namespace Notes.Services;

public sealed class NoteFolderService : INoteFolderService
{
    private readonly IFileSystem _fileSystem;
    private readonly IPathGuard _pathGuard;

    public NoteFolderService(IFileSystem fileSystem, IPathGuard pathGuard)
    {
        _fileSystem = fileSystem;
        _pathGuard = pathGuard;
    }

    public void Create(string absolutePath)
    {
        _pathGuard.EnsureWithinWorkspace(absolutePath);
        _fileSystem.Directory.CreateDirectory(absolutePath);
    }
}
