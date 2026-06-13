namespace Notes.Core.Services;

public interface INoteDeleter
{
    void Delete(string absolutePath);

    void DeleteFolder(string absolutePath);
}
