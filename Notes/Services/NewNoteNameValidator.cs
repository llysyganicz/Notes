using System;
using System.IO;

namespace Notes.Services;

public sealed class NewNoteNameValidator : INewNoteNameValidator
{
    private const string MdExtension = ".md";

    public NoteNameResult Validate(string rawInput, string workspaceAbsolutePath, string parentRelativePath)
    {
        var trimmed = (rawInput ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new NoteNameResult.Failure("Name cannot be empty");
        }

        if (trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            return new NoteNameResult.Failure("Name contains an invalid character");
        }

        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            if (trimmed.Contains(ch))
            {
                return new NoteNameResult.Failure("Name contains an invalid character");
            }
        }

        var fileName = trimmed.EndsWith(MdExtension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + MdExtension;

        var parentSegment = string.IsNullOrEmpty(parentRelativePath)
            ? string.Empty
            : parentRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = string.IsNullOrEmpty(parentSegment)
            ? Path.Combine(workspaceAbsolutePath, fileName)
            : Path.Combine(workspaceAbsolutePath, parentSegment, fileName);

        if (File.Exists(absolutePath))
        {
            return new NoteNameResult.Failure("A note with that name already exists");
        }

        return new NoteNameResult.Success(fileName);
    }
}
