using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class NoteMetadataParserTests
{
    private readonly NoteMetadataParser _parser = new();

    [Fact]
    public void Parse_WhenInputIsNull_ReturnsEmpty()
    {
        var result = _parser.Parse(null);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenInputIsEmpty_ReturnsEmpty()
    {
        var result = _parser.Parse(string.Empty);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenNoFrontmatter_ReturnsZeroTags()
    {
        var result = _parser.Parse("# Just a heading\n\nWith body text.");

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenFrontmatterButNoTagsKey_ReturnsZeroTags()
    {
        var text = "---\ntitle: My Note\ndate: 2026-01-01\n---\n\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsFlowList_ReturnsTagsLowercased()
    {
        var text = "---\ntags: [project, urgent]\n---\n\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "project", "urgent" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsBlockList_ReturnsTagsLowercased()
    {
        var text = "---\ntags:\n- project\n- urgent\n---\n\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "project", "urgent" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsContainMixedCase_ReturnsAllLowercased()
    {
        var text = "---\ntags: [Project, URGENT, ToDo]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "project", "urgent", "todo" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsContainWhitespaceValue_DropsThatTag()
    {
        var text = "---\ntags: [\"foo bar\", baz]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "baz" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsContainHyphens_KeepsThemAsCanonical()
    {
        var text = "---\ntags: [in-progress, side-project-2]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "in-progress", "side-project-2" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsContainUnderscoreOrPunctuation_DropsThoseTags()
    {
        var text = "---\ntags: [foo_bar, hello!, baz, \"a.b\"]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "baz" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsContainEmptyOrNullValue_DropsThoseEntries()
    {
        var text = "---\ntags: [\"\", ~, a]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "a" }, result.Tags);
    }

    [Fact]
    public void Parse_WhenTagsKeyIsNotAList_ReturnsZeroTags()
    {
        var text = "---\ntags: just-a-string\n---\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenFrontmatterMalformed_ReturnsZeroTags()
    {
        var text = "---\ntags: [unclosed\n---\n\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Tags);
    }

    [Fact]
    public void Parse_WhenDuplicateTags_DeduplicatesPreservingFirstOccurrence()
    {
        var text = "---\ntags: [Foo, FOO, foo, bar, Bar]\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "foo", "bar" }, result.Tags);
    }
}
