using System.IO;

namespace Notes.Services;

public sealed class NoteDeleter : INoteDeleter
{
    public void Delete(string absolutePath) => File.Delete(absolutePath);
}
