using System;
using System.Collections.Generic;
using System.IO;

namespace Notes.Services;

public sealed class WorkspaceScanner : IWorkspaceScanner
{
    public IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
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
        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*.md", enumerationOptions))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith('.'))
            {
                continue;
            }

            var relative = Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
            results.Add(relative);
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }
}
