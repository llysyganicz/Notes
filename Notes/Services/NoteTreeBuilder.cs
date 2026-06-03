using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Notes.Models;

namespace Notes.Services;

public sealed class NoteTreeBuilder
{
    private readonly IFileSystem _fileSystem;

    public NoteTreeBuilder(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public NoteTreeNode Build(string rootDirectory, IReadOnlyList<string> relativePaths)
    {
        var directoryPaths = EnumerateDirectories(rootDirectory);
        return BuildNode(name: string.Empty, relativePath: string.Empty, relativePaths, directoryPaths);
    }

    private IReadOnlyList<string> EnumerateDirectories(string rootDirectory)
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
        foreach (var path in _fileSystem.Directory.EnumerateDirectories(rootDirectory, "*", enumerationOptions))
        {
            var relative = _fileSystem.Path.GetRelativePath(rootDirectory, path).Replace('\\', '/');
            results.Add(relative);
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static NoteTreeNode BuildNode(
        string name,
        string relativePath,
        IReadOnlyList<string> filePaths,
        IReadOnlyList<string> directoryPaths)
    {
        var folderGroups = new SortedDictionary<string, FolderGroup>(StringComparer.OrdinalIgnoreCase);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in filePaths)
        {
            var separator = path.IndexOf('/');
            if (separator < 0)
            {
                files.Add(path);
            }
            else
            {
                var head = path[..separator];
                var tail = path[(separator + 1)..];
                GetGroup(folderGroups, head).Files.Add(tail);
            }
        }

        foreach (var path in directoryPaths)
        {
            var separator = path.IndexOf('/');
            if (separator < 0)
            {
                // Leaf directory at this level — ensure its node exists even with no files.
                GetGroup(folderGroups, path);
            }
            else
            {
                var head = path[..separator];
                var tail = path[(separator + 1)..];
                GetGroup(folderGroups, head).Directories.Add(tail);
            }
        }

        var children = new List<NoteTreeNode>(folderGroups.Count + files.Count);

        foreach (var (folderName, group) in folderGroups)
        {
            var folderRel = string.IsNullOrEmpty(relativePath) ? folderName : $"{relativePath}/{folderName}";
            children.Add(BuildNode(folderName, folderRel, group.Files, group.Directories));
        }

        foreach (var fileName in files)
        {
            var fileRel = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
            children.Add(new NoteTreeNode(fileName, fileRel, NoteNodeKind.File, Array.Empty<NoteTreeNode>()));
        }

        return new NoteTreeNode(name, relativePath, NoteNodeKind.Folder, children);
    }

    private static FolderGroup GetGroup(SortedDictionary<string, FolderGroup> groups, string head)
    {
        if (!groups.TryGetValue(head, out var group))
        {
            group = new FolderGroup();
            groups[head] = group;
        }

        return group;
    }

    private sealed class FolderGroup
    {
        public List<string> Files { get; } = new();
        public List<string> Directories { get; } = new();
    }
}
