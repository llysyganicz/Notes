using System;
using System.IO;
using System.IO.Abstractions;

namespace Notes.Services;

public sealed class NameValidator : INameValidator
{
    private const string MdExtension = ".md";

    private readonly IFileSystem _fileSystem;

    public NameValidator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public NoteNameResult ValidateNoteName(string rawInput, string workspaceAbsolutePath, string parentRelativePath)
    {
        if (ValidateCharacters(rawInput) is { } failure)
        {
            return failure;
        }

        var trimmed = rawInput.Trim();
        var fileName = trimmed.EndsWith(MdExtension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + MdExtension;

        var absolutePath = ResolveAbsolutePath(workspaceAbsolutePath, parentRelativePath, fileName);
        if (_fileSystem.File.Exists(absolutePath))
        {
            return new NoteNameResult.Failure("A note with that name already exists");
        }

        return new NoteNameResult.Success(fileName, absolutePath);
    }

    public NoteNameResult ValidateFolderName(string rawInput, string workspaceAbsolutePath, string parentRelativePath)
    {
        if (ValidateCharacters(rawInput) is { } failure)
        {
            return failure;
        }

        var folderName = rawInput.Trim();
        var absolutePath = ResolveAbsolutePath(workspaceAbsolutePath, parentRelativePath, folderName);
        if (_fileSystem.Directory.Exists(absolutePath) || _fileSystem.File.Exists(absolutePath))
        {
            return new NoteNameResult.Failure("A folder with that name already exists");
        }

        return new NoteNameResult.Success(folderName, absolutePath);
    }

    private static NoteNameResult.Failure? ValidateCharacters(string rawInput)
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

        return null;
    }

    private static string ResolveAbsolutePath(string workspaceAbsolutePath, string parentRelativePath, string name)
    {
        var parentSegment = string.IsNullOrEmpty(parentRelativePath)
            ? string.Empty
            : parentRelativePath.Replace('/', Path.DirectorySeparatorChar);

        return string.IsNullOrEmpty(parentSegment)
            ? Path.Combine(workspaceAbsolutePath, name)
            : Path.Combine(workspaceAbsolutePath, parentSegment, name);
    }
}
