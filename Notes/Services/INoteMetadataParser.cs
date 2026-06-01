using Notes.Models;

namespace Notes.Services;

public interface INoteMetadataParser
{
    NoteMetadata Parse(string noteText);
}
