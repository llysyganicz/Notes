using System.Collections.Generic;
using Notes.Models;
using Notes.Services;
using Xunit;

namespace Notes.Tests;

public sealed class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    private static FormDefinition Definition(params string[] names)
    {
        var fields = new List<FormFieldEntry>();
        foreach (var name in names)
        {
            fields.Add(new FormFieldEntry(name, new FormField("text", name)));
        }

        return new FormDefinition(fields);
    }

    [Fact]
    public void Render_WhenDeclaredTokenInBody_SubstitutesValue()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\n# {{name}}\n";
        var values = new Dictionary<string, string> { ["name"] = "Project X" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("# Project X\n", result);
    }

    [Fact]
    public void Render_WhenTokenUndeclared_LeavesItVerbatim()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\nHello {{name}} and {{other}}\n";
        var values = new Dictionary<string, string> { ["name"] = "World" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("Hello World and {{other}}\n", result);
    }

    [Fact]
    public void Render_WhenValueMissing_SubstitutesEmptyString()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\nValue: [{{name}}]\n";
        var values = new Dictionary<string, string>();

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("Value: []\n", result);
    }

    [Fact]
    public void Render_WhenValueBlank_SubstitutesEmptyString()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\nValue: [{{name}}]\n";
        var values = new Dictionary<string, string> { ["name"] = string.Empty };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("Value: []\n", result);
    }

    [Fact]
    public void Render_WhenFormIsOnlyFrontmatterKey_DropsTheFence()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\n# {{name}}\n\nBody\n";
        var values = new Dictionary<string, string> { ["name"] = "Title" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("# Title\n\nBody\n", result);
    }

    [Fact]
    public void Render_WhenOtherFrontmatterKeysPresent_PreservesThemVerbatim()
    {
        var template =
            "---\ntitle: My Note\nform:\n  name:\n    type: text\n    label: Name\ndate: 2026-01-01\n---\n# {{name}}\n";
        var values = new Dictionary<string, string> { ["name"] = "Hello" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("---\ntitle: My Note\ndate: 2026-01-01\n---\n# Hello\n", result);
    }

    [Fact]
    public void Render_WhenFormBlockComesBeforeOtherKeys_StripsOnlyFormBlock()
    {
        var template =
            "---\nform:\n  name:\n    type: text\n    label: Name\ntitle: Kept\n---\nBody {{name}}\n";
        var values = new Dictionary<string, string> { ["name"] = "X" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("---\ntitle: Kept\n---\nBody X\n", result);
    }

    [Fact]
    public void Render_WhenTokenInFrontmatter_DoesNotSubstituteIt()
    {
        var template =
            "---\ntitle: {{name}}\nform:\n  name:\n    type: text\n    label: Name\n---\nBody {{name}}\n";
        var values = new Dictionary<string, string> { ["name"] = "Sub" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("---\ntitle: {{name}}\n---\nBody Sub\n", result);
    }

    [Fact]
    public void Render_WhenTemplateHasNoFrontmatter_SubstitutesBodyOnly()
    {
        var template = "# {{name}}\n\nNo frontmatter here.\n";
        var values = new Dictionary<string, string> { ["name"] = "Plain" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("# Plain\n\nNo frontmatter here.\n", result);
    }

    [Fact]
    public void Render_WhenDefinitionEmptyAndFormMalformed_StripsFormLineLeavingStaticCopy()
    {
        // Malformed/absent form parses to an empty definition; render is a static copy
        // that still strips a stray form line and substitutes nothing.
        var template = "---\nform: garbage\ntitle: Kept\n---\nBody {{name}}\n";
        var values = new Dictionary<string, string>();

        var result = _renderer.Render(template, FormDefinition.Empty, values);

        Assert.Equal("---\ntitle: Kept\n---\nBody {{name}}\n", result);
    }

    [Fact]
    public void Render_WhenFormBlockContainsBlankLine_StripsEntireBlock()
    {
        var template =
            "---\nform:\n  a:\n    type: text\n    label: A\n\n  b:\n    type: text\n    label: B\ntitle: Kept\n---\nBody {{a}}\n";
        var values = new Dictionary<string, string> { ["a"] = "X", ["b"] = "Y" };

        var result = _renderer.Render(template, Definition("a", "b"), values);

        Assert.Equal("---\ntitle: Kept\n---\nBody X\n", result);
    }

    [Fact]
    public void Render_WhenSiblingKeyStartsWithForm_PreservesIt()
    {
        var template =
            "---\nform:\n  name:\n    type: text\n    label: Name\nformat: pretty\n---\nBody {{name}}\n";
        var values = new Dictionary<string, string> { ["name"] = "X" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("---\nformat: pretty\n---\nBody X\n", result);
    }

    [Fact]
    public void Render_WhenTemplateUsesCrlf_PreservesCrlfEndings()
    {
        var template =
            "---\r\ntitle: My Note\r\nform:\r\n  name:\r\n    type: text\r\n    label: Name\r\n---\r\nBody {{name}}\r\nNext line\r\n";
        var values = new Dictionary<string, string> { ["name"] = "X" };

        var result = _renderer.Render(template, Definition("name"), values);

        Assert.Equal("---\r\ntitle: My Note\r\n---\r\nBody X\r\nNext line\r\n", result);
    }

    [Fact]
    public void Render_WhenMultipleDeclaredTokens_SubstitutesEachInOrder()
    {
        var template =
            "---\nform:\n  a:\n    type: text\n    label: A\n  b:\n    type: text\n    label: B\n---\n{{a}}-{{b}}-{{a}}\n";
        var values = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        var result = _renderer.Render(template, Definition("a", "b"), values);

        Assert.Equal("1-2-1\n", result);
    }
}
