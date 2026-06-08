using System;
using System.IO;
using System.IO.Abstractions;

namespace Notes.Services;

public sealed class NameValidator : INameValidator
{
    private const string MdExtension = ".md";

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

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

        if (trimmed == "." || trimmed == "..")
        {
            return new NoteNameResult.Failure("Name contains an invalid character");
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(trimmed);
        if (Array.Exists(ReservedNames, r => r.Equals(nameWithoutExt, StringComparison.OrdinalIgnoreCase)))
        {
            return new NoteNameResult.Failure("Name is reserved and cannot be used");
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
