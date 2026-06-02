using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        return File.ReadAllText(absolutePath);
    }

    public Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(absolutePath))
        {
            return Task.FromResult(string.Empty);
        }

        return File.ReadAllTextAsync(absolutePath, Utf8NoBom, cancellationToken);
    }

    public void Save(string absolutePath, string text)
    {
        File.WriteAllText(absolutePath, text, Utf8NoBom);
    }
}
