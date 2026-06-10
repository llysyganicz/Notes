using Notes.Core.Models;

namespace Notes.Core.Services;

public interface INoteMetadataParser
{
    NoteMetadata Parse(string? noteText);
}
