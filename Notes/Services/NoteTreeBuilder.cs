using System;
using System.Collections.Generic;
using Notes.Models;

namespace Notes.Services;

public sealed class NoteTreeBuilder
{
    public NoteTreeNode Build(IReadOnlyList<string> relativePaths)
    {
        return BuildNode(name: string.Empty, relativePath: string.Empty, relativePaths);
    }

    private static NoteTreeNode BuildNode(string name, string relativePath, IReadOnlyList<string> paths)
    {
        var folderGroups = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
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
                if (!folderGroups.TryGetValue(head, out var list))
                {
                    list = new List<string>();
                    folderGroups[head] = list;
                }

                list.Add(tail);
            }
        }

        var children = new List<NoteTreeNode>(folderGroups.Count + files.Count);

        foreach (var (folderName, subPaths) in folderGroups)
        {
            var folderRel = string.IsNullOrEmpty(relativePath) ? folderName : $"{relativePath}/{folderName}";
            children.Add(BuildNode(folderName, folderRel, subPaths));
        }

        foreach (var fileName in files)
        {
            var fileRel = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
            children.Add(new NoteTreeNode(fileName, fileRel, NoteNodeKind.File, Array.Empty<NoteTreeNode>()));
        }

        return new NoteTreeNode(name, relativePath, NoteNodeKind.Folder, children);
    }
}
