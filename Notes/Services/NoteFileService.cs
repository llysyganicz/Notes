using System.IO;
using System.Text;

namespace Notes.Services;

public sealed class NoteFileService : INoteFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Read(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return string.Empty;
        }

        return File.ReadAllText(absolutePath, Encoding.UTF8);
    }

    public void Save(string absolutePath, string text)
    {
        File.WriteAllText(absolutePath, text, Utf8NoBom);
    }
}
