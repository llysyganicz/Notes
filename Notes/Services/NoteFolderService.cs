using System.IO.Abstractions;

namespace Notes.Services;

public sealed class NoteFolderService : INoteFolderService
{
    private readonly IFileSystem _fileSystem;

    public NoteFolderService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public void Create(string absolutePath) => _fileSystem.Directory.CreateDirectory(absolutePath);
}
