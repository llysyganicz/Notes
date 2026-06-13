using System.Collections.Generic;

namespace Notes.Core.Models;

public enum NoteNodeKind
{
    Folder,
    File,
}

public sealed record NoteTreeNode(
    string Name,
    string RelativePath,
    NoteNodeKind Kind,
    IReadOnlyList<NoteTreeNode> Children);
