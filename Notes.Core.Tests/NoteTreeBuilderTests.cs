using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Notes.Core.Models;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

public sealed class NoteTreeBuilderTests
{
    // A root that is never created on the mock filesystem: directory enumeration
    // contributes nothing, so these cases exercise the file-path logic in isolation.
    private const string MissingRoot = "/workspace";

    private readonly MockFileSystem _fileSystem = new();
    private NoteTreeBuilder Builder => new(_fileSystem);

    [Fact]
    public void Build_WhenInputEmpty_ReturnsRootWithNoChildren()
    {
        var tree = Builder.Build(MissingRoot, new List<string>());

        Assert.Equal(string.Empty, tree.Name);
        Assert.Equal(string.Empty, tree.RelativePath);
        Assert.Equal(NoteNodeKind.Folder, tree.Kind);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void Build_WhenRootLevelFile_ReturnsFileChild()
    {
        var tree = Builder.Build(MissingRoot, new[] { "notes.md" });

        var child = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.File, child.Kind);
        Assert.Equal("notes.md", child.Name);
        Assert.Equal("notes.md", child.RelativePath);
    }

    [Fact]
    public void Build_WhenNestedFile_CreatesIntermediateFolderNodes()
    {
        var tree = Builder.Build(MissingRoot, new[] { "a/b/c.md" });

        var a = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.Folder, a.Kind);
        Assert.Equal("a", a.Name);
        Assert.Equal("a", a.RelativePath);

        var b = Assert.Single(a.Children);
        Assert.Equal(NoteNodeKind.Folder, b.Kind);
        Assert.Equal("b", b.Name);
        Assert.Equal("a/b", b.RelativePath);

        var c = Assert.Single(b.Children);
        Assert.Equal(NoteNodeKind.File, c.Kind);
        Assert.Equal("c.md", c.Name);
        Assert.Equal("a/b/c.md", c.RelativePath);
    }

    [Fact]
    public void Build_WhenFoldersAndFilesAtSameLevel_SortsFoldersFirst()
    {
        var tree = Builder.Build(MissingRoot, new[] { "zzz.md", "aaa/file.md" });

        Assert.Equal(2, tree.Children.Count);
        Assert.Equal(NoteNodeKind.Folder, tree.Children[0].Kind);
        Assert.Equal("aaa", tree.Children[0].Name);
        Assert.Equal(NoteNodeKind.File, tree.Children[1].Kind);
        Assert.Equal("zzz.md", tree.Children[1].Name);
    }

    [Fact]
    public void Build_WhenSameFolderNameAtDifferentDepths_KeepsThemDistinct()
    {
        var tree = Builder.Build(MissingRoot, new[] { "sub/x.md", "outer/sub/y.md" });

        var outer = tree.Children.Single(c => c.Name == "outer");
        var topSub = tree.Children.Single(c => c.Name == "sub");
        var innerSub = outer.Children.Single(c => c.Name == "sub");

        Assert.NotSame(topSub, innerSub);
        Assert.Equal("sub", topSub.RelativePath);
        Assert.Equal("outer/sub", innerSub.RelativePath);
    }

    [Fact]
    public void Build_WhenCalled_SortsChildrenAlphabetically()
    {
        var tree = Builder.Build(MissingRoot, new[] { "delta.md", "alpha.md", "charlie.md", "bravo.md" });

        var names = tree.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "alpha.md", "bravo.md", "charlie.md", "delta.md" }, names);
    }

    [Fact]
    public void Build_WhenMixedCase_SortsCaseInsensitively()
    {
        var tree = Builder.Build(MissingRoot, new[] { "Banana.md", "apple.md" });

        var names = tree.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "apple.md", "Banana.md" }, names);
    }

    [Fact]
    public void Build_WhenEmptyDirectoryOnDisk_YieldsFolderNode()
    {
        _fileSystem.AddDirectory("/ws/empty");

        var tree = Builder.Build("/ws", new List<string>());

        var folder = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.Folder, folder.Kind);
        Assert.Equal("empty", folder.Name);
        Assert.Equal("empty", folder.RelativePath);
        Assert.Empty(folder.Children);
    }

    [Fact]
    public void Build_WhenDirectoryHasFilesAndExistsOnDisk_YieldsSingleNode()
    {
        _fileSystem.AddDirectory("/ws/sub");

        var tree = Builder.Build("/ws", new[] { "sub/note.md" });

        var folder = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.Folder, folder.Kind);
        Assert.Equal("sub", folder.Name);

        var file = Assert.Single(folder.Children);
        Assert.Equal(NoteNodeKind.File, file.Kind);
        Assert.Equal("note.md", file.Name);
        Assert.Equal("sub/note.md", file.RelativePath);
    }

    [Fact]
    public void Build_WhenDotDirectoryOnDisk_IncludesItAsFolder()
    {
        _fileSystem.AddDirectory("/ws/.templates");

        var tree = Builder.Build("/ws", new List<string>());

        var folder = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.Folder, folder.Kind);
        Assert.Equal(".templates", folder.Name);
        Assert.Equal(".templates", folder.RelativePath);
    }

    [Fact]
    public void Build_WhenNestedEmptyDirectoryOnDisk_YieldsNestedFolderNodes()
    {
        _fileSystem.AddDirectory("/ws/outer/inner");

        var tree = Builder.Build("/ws", new List<string>());

        var outer = Assert.Single(tree.Children);
        Assert.Equal("outer", outer.Name);
        Assert.Equal("outer", outer.RelativePath);

        var inner = Assert.Single(outer.Children);
        Assert.Equal(NoteNodeKind.Folder, inner.Kind);
        Assert.Equal("inner", inner.Name);
        Assert.Equal("outer/inner", inner.RelativePath);
        Assert.Empty(inner.Children);
    }

    [Fact]
    public void Build_WhenEmptyAndFileBearingFoldersMixed_SortsFoldersFirstThenFiles()
    {
        _fileSystem.AddDirectory("/ws/zfolder");

        var tree = Builder.Build("/ws", new[] { "afolder/note.md", "root.md" });

        Assert.Equal(3, tree.Children.Count);
        Assert.Equal(NoteNodeKind.Folder, tree.Children[0].Kind);
        Assert.Equal("afolder", tree.Children[0].Name);
        Assert.Equal(NoteNodeKind.Folder, tree.Children[1].Kind);
        Assert.Equal("zfolder", tree.Children[1].Name);
        Assert.Equal(NoteNodeKind.File, tree.Children[2].Kind);
        Assert.Equal("root.md", tree.Children[2].Name);
    }
}
