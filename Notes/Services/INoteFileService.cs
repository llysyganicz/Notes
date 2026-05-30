namespace Notes.Services;

public interface INoteFileService
{
    string Read(string absolutePath);
    void Save(string absolutePath, string text);
}
