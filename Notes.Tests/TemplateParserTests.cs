using System.Linq;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class TemplateParserTests
{
    private readonly TemplateParser _parser = new();

    [Fact]
    public void Parse_WhenInputIsNull_ReturnsEmpty()
    {
        var result = _parser.Parse(null);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenInputIsEmpty_ReturnsEmpty()
    {
        var result = _parser.Parse(string.Empty);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenNoFrontmatter_ReturnsEmpty()
    {
        var result = _parser.Parse("# Heading\n\nBody {{x}}\n");

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFrontmatterHasNoFormKey_ReturnsEmpty()
    {
        var text = "---\ntitle: My Note\ntags: [a, b]\n---\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFormWellFormed_ReturnsFieldsInDocumentOrder()
    {
        var text =
            "---\n" +
            "form:\n" +
            "  project_name:\n" +
            "    type: text\n" +
            "    label: Project name\n" +
            "  priority:\n" +
            "    type: select\n" +
            "    label: Priority\n" +
            "    entries: [low, medium, high]\n" +
            "  due:\n" +
            "    type: date\n" +
            "    label: Due\n" +
            "---\n# {{project_name}}\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "project_name", "priority", "due" }, result.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Parse_WhenFieldHasTypeAndLabel_CapturesThem()
    {
        var text =
            "---\nform:\n  project_name:\n    type: text\n    label: Project name\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal("text", field.Type);
        Assert.Equal("Project name", field.Label);
    }

    [Fact]
    public void Parse_WhenSelectField_CapturesEntries()
    {
        var text =
            "---\nform:\n  priority:\n    type: select\n    label: Priority\n    entries: [low, medium, high]\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal(new[] { "low", "medium", "high" }, field.Entries);
    }

    [Fact]
    public void Parse_WhenDateFieldHasFormat_CapturesFormat()
    {
        var text =
            "---\nform:\n  due:\n    type: date\n    label: Due\n    format: dd/MM/yyyy\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal("dd/MM/yyyy", field.Format);
    }

    [Fact]
    public void Parse_WhenNumberFieldWithFormat_CapturesTypeAndFormat()
    {
        var text =
            "---\nform:\n  amount:\n    type: number\n    label: Amount\n    format: F2\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal("number", field.Type);
        Assert.Equal("F2", field.Format);
    }

    [Fact]
    public void Parse_WhenNonDropdownField_HasNullEntries()
    {
        var text = "---\nform:\n  name:\n    type: text\n    label: Name\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Null(field.Entries);
    }

    [Fact]
    public void Parse_WhenFrontmatterMalformed_ReturnsEmpty()
    {
        var text = "---\nform:\n  x: [unclosed\n---\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFormIsScalarNotMap_ReturnsEmpty()
    {
        var text = "---\nform: just-a-string\n---\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFormCoexistsWithOtherKeys_ParsesOnlyFormFields()
    {
        var text =
            "---\ntitle: My Note\nform:\n  name:\n    type: text\n    label: Name\ndate: 2026-01-01\n---\n";

        var result = _parser.Parse(text);

        Assert.Equal(new[] { "name" }, result.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Parse_WhenFormWellFormed_PopulatesNamesSet()
    {
        var text =
            "---\nform:\n  a:\n    type: text\n    label: A\n  b:\n    type: text\n    label: B\n---\n";

        var result = _parser.Parse(text);

        Assert.True(result.Names.Contains("a"));
        Assert.True(result.Names.Contains("b"));
        Assert.False(result.Names.Contains("c"));
    }

    [Fact]
    public void Parse_WhenFormIsSequenceNotMap_ReturnsEmpty()
    {
        var text = "---\nform:\n  - item1\n  - item2\n---\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFormIsTabIndented_ReturnsEmpty()
    {
        var text = "---\nform:\n\tfield1:\n\t\ttype: text\n---\n# Body\n";

        var result = _parser.Parse(text);

        Assert.Empty(result.Fields);
    }

    [Fact]
    public void Parse_WhenFieldMissingType_HasEmptyType()
    {
        var text = "---\nform:\n  name:\n    label: Name\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal(string.Empty, field.Type);
    }

    [Fact]
    public void Parse_WhenFieldMissingLabel_HasEmptyLabel()
    {
        var text = "---\nform:\n  name:\n    type: text\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.Equal(string.Empty, field.Label);
    }

    [Fact]
    public void Parse_WhenSelectFieldHasNoEntries_EntriesIsNullOrEmpty()
    {
        var text = "---\nform:\n  priority:\n    type: select\n    label: Priority\n---\n";

        var field = _parser.Parse(text).Fields.Single().Field;

        Assert.True(field.Entries is null || field.Entries.Count == 0);
    }
}
