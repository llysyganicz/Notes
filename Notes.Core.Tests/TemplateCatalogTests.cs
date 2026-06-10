using System;
using System.Linq;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

public sealed class TemplateCatalogTests
{
    private static TemplateCatalog Loaded(params string[] paths)
    {
        var catalog = new TemplateCatalog();
        catalog.Load(paths);
        return catalog;
    }

    [Fact]
    public void List_WhenTopLevelTemplatesLoaded_ReturnsThem()
    {
        var catalog = Loaded(".templates/daily.md", ".templates/meeting.md", "notes/other.md", "root.md");

        Assert.Equal(
            new[] { ".templates/daily.md", ".templates/meeting.md" },
            catalog.List().Select(t => t.RelativePath).ToArray());
    }

    [Fact]
    public void List_WhenCalled_SetsDisplayNameToFileName()
    {
        var catalog = Loaded(".templates/daily.md");

        var info = Assert.Single(catalog.List());
        Assert.Equal("daily.md", info.DisplayName);
    }

    [Fact]
    public void List_WhenNestedUnderTemplates_SkipsDeeperEntries()
    {
        var catalog = Loaded(".templates/daily.md", ".templates/sub/nested.md");

        var info = Assert.Single(catalog.List());
        Assert.Equal(".templates/daily.md", info.RelativePath);
    }

    [Fact]
    public void List_WhenNoTemplatesLoaded_ReturnsEmpty()
    {
        var catalog = Loaded("root.md", "notes/other.md");

        Assert.Empty(catalog.List());
    }

    [Fact]
    public void List_WhenNeverLoaded_ReturnsEmpty()
    {
        Assert.Empty(new TemplateCatalog().List());
    }

    [Fact]
    public void HasAny_WhenTemplatePresent_ReturnsTrue()
    {
        Assert.True(Loaded(".templates/daily.md").HasAny());
    }

    [Fact]
    public void HasAny_WhenNoTemplates_ReturnsFalse()
    {
        Assert.False(Loaded("root.md").HasAny());
    }

    [Fact]
    public void Load_WhenCalledAgain_ReplacesPreviousSet()
    {
        var catalog = Loaded(".templates/daily.md");
        Assert.True(catalog.HasAny());

        catalog.Load(new[] { "root.md" });

        Assert.False(catalog.HasAny());
        Assert.Empty(catalog.List());
    }
}
