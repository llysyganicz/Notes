using System.Collections.Generic;
using System.Linq;
using Notes.Models;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NoteTreeBuilderTests
{
    private readonly NoteTreeBuilder _builder = new();

    [Fact]
    public void Build_WhenInputEmpty_ReturnsRootWithNoChildren()
    {
        var tree = _builder.Build(new List<string>());

        Assert.Equal(string.Empty, tree.Name);
        Assert.Equal(string.Empty, tree.RelativePath);
        Assert.Equal(NoteNodeKind.Folder, tree.Kind);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void Build_WhenRootLevelFile_ReturnsFileChild()
    {
        var tree = _builder.Build(new[] { "notes.md" });

        var child = Assert.Single(tree.Children);
        Assert.Equal(NoteNodeKind.File, child.Kind);
        Assert.Equal("notes.md", child.Name);
        Assert.Equal("notes.md", child.RelativePath);
    }

    [Fact]
    public void Build_WhenNestedFile_CreatesIntermediateFolderNodes()
    {
        var tree = _builder.Build(new[] { "a/b/c.md" });

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
        var tree = _builder.Build(new[] { "zzz.md", "aaa/file.md" });

        Assert.Equal(2, tree.Children.Count);
        Assert.Equal(NoteNodeKind.Folder, tree.Children[0].Kind);
        Assert.Equal("aaa", tree.Children[0].Name);
        Assert.Equal(NoteNodeKind.File, tree.Children[1].Kind);
        Assert.Equal("zzz.md", tree.Children[1].Name);
    }

    [Fact]
    public void Build_WhenSameFolderNameAtDifferentDepths_KeepsThemDistinct()
    {
        var tree = _builder.Build(new[] { "sub/x.md", "outer/sub/y.md" });

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
        var tree = _builder.Build(new[] { "delta.md", "alpha.md", "charlie.md", "bravo.md" });

        var names = tree.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "alpha.md", "bravo.md", "charlie.md", "delta.md" }, names);
    }

    [Fact]
    public void Build_WhenMixedCase_SortsCaseInsensitively()
    {
        var tree = _builder.Build(new[] { "Banana.md", "apple.md" });

        var names = tree.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "apple.md", "Banana.md" }, names);
    }
}
