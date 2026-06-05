using System.IO.Abstractions;

namespace Notes.Services;

public sealed class NoteDeleter : INoteDeleter
{
    private readonly IFileSystem _fileSystem;

    public NoteDeleter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public void Delete(string absolutePath) => _fileSystem.File.Delete(absolutePath);

    public void DeleteFolder(string absolutePath) => _fileSystem.Directory.Delete(absolutePath, recursive: true);
}
