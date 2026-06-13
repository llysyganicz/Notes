using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace Notes.Core.Services;

public sealed class WorkspaceScanner : IWorkspaceScanner
{
    private readonly IFileSystem _fileSystem;

    public WorkspaceScanner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory)
    {
        if (!_fileSystem.Directory.Exists(rootDirectory))
        {
            return Array.Empty<string>();
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };

        var results = new List<string>();
        foreach (var path in _fileSystem.Directory.EnumerateFiles(rootDirectory, "*.md", enumerationOptions))
        {
            var fileName = _fileSystem.Path.GetFileName(path);
            if (fileName.StartsWith('.'))
            {
                continue;
            }

            var relative = _fileSystem.Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
            results.Add(relative);
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }
}
