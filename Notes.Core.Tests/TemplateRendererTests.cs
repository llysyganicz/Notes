using System.Collections.Generic;
using System.Linq;
using Notes.Core.Models;
using Notes.Core.Services;
using Xunit;

namespace Notes.Core.Tests;

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

    // Opening `---` with no closing `---` → not a frontmatter block; the whole text is body.
    // Kills the block-removal of the `closing < 0` early return (without it, GetRange(1, -2) throws).
    [Fact]
    public void Render_WhenFrontmatterFenceUnclosed_TreatsWholeTextAsBody()
    {
        const string template = "---\nkey: value\nbody {{name}}\n";

        var result = _renderer.Render(
            template, Definition("name"),
            new Dictionary<string, string> { ["name"] = "X" });

        Assert.Equal("---\nkey: value\nbody X\n", result);
    }

    public sealed record RenderCase(
        string Template,
        string[] FieldNames,
        Dictionary<string, string> Values,
        string Expected);

    // Phase 2 — §2.1: token substitution rules.
    // Expected is built from (template + definition + values), never copied from renderer output.
    // Covers: zero leftover declared tokens, mis-cased token stays verbatim, undeclared verbatim, slot fidelity.
    public static TheoryData<RenderCase> TokenSubstitutionCases =>
        new()
        {
            // All declared tokens substituted → zero leftover declared tokens in body
            new RenderCase(
                "---\nform:\n  title:\n    type: text\n    label: Title\n  author:\n    type: text\n    label: Author\n---\n# {{title}}\nBy {{author}}\n",
                new[] { "title", "author" },
                new Dictionary<string, string> { ["title"] = "My Note", ["author"] = "Alice" },
                "# My Note\nBy Alice\n"),
            // Mis-cased {{Title}} is not declared 'title' (ordinal comparison) → stays verbatim; {{title}} IS substituted
            new RenderCase(
                "---\nform:\n  title:\n    type: text\n    label: Title\n---\n{{Title}}-{{title}}\n",
                new[] { "title" },
                new Dictionary<string, string> { ["title"] = "Sub" },
                "{{Title}}-Sub\n"),
            // Undeclared token stays verbatim alongside declared substitution
            new RenderCase(
                "---\nform:\n  name:\n    type: text\n    label: Name\n---\nHello {{name}} and {{extra}}\n",
                new[] { "name" },
                new Dictionary<string, string> { ["name"] = "World" },
                "Hello World and {{extra}}\n"),
            // Slot fidelity: two distinct values land only in their own slots; duplicate occurrence consistent
            new RenderCase(
                "---\nform:\n  a:\n    type: text\n    label: A\n  b:\n    type: text\n    label: B\n---\n{{b}}-{{a}}-{{b}}-{{a}}\n",
                new[] { "a", "b" },
                new Dictionary<string, string> { ["a"] = "ALPHA", ["b"] = "BETA" },
                "BETA-ALPHA-BETA-ALPHA\n")
        };

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

    // The body deliberately has NO trailing newline so this also pins last-line terminator
    // fidelity: it kills the SplitLines no-final-newline branch (dropping the last line,
    // mangling its empty terminator, or removing the loop break).
    [Fact]
    public void Render_WhenMultipleDeclaredTokens_SubstitutesEachInOrder()
    {
        var template =
            "---\nform:\n  a:\n    type: text\n    label: A\n  b:\n    type: text\n    label: B\n---\n{{a}}-{{b}}-{{a}}";
        var values = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        var result = _renderer.Render(template, Definition("a", "b"), values);

        Assert.Equal("1-2-1", result);
    }

    [Fact]
    public void RenderBody_WhenFrontmatterAndBody_ReturnsSubstitutedBodyOnly()
    {
        var template =
            "---\nform:\n  name:\n    type: text\n    label: Name\ntitle: Kept\n---\n# {{name}}\nBody line\n";
        var values = new Dictionary<string, string> { ["name"] = "World" };

        var result = _renderer.RenderBody(template, Definition("name"), values);

        Assert.Equal("# World\nBody line\n", result);
    }

    [Fact]
    public void RenderBody_WhenNoFrontmatter_ReturnsSubstitutedFullText()
    {
        var template = "# {{name}}\nNo frontmatter here.\n";
        var values = new Dictionary<string, string> { ["name"] = "Plain" };

        var result = _renderer.RenderBody(template, Definition("name"), values);

        Assert.Equal("# Plain\nNo frontmatter here.\n", result);
    }

    [Fact]
    public void RenderBody_WhenOnlyFrontmatter_ReturnsEmptyString()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\n";
        var values = new Dictionary<string, string> { ["name"] = "Unused" };

        var result = _renderer.RenderBody(template, Definition("name"), values);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RenderBody_WhenUndeclaredToken_LeavesItVerbatim()
    {
        var template = "---\nform:\n  name:\n    type: text\n    label: Name\n---\nHello {{name}} and {{extra}}\n";
        var values = new Dictionary<string, string> { ["name"] = "World" };

        var result = _renderer.RenderBody(template, Definition("name"), values);

        Assert.Equal("Hello World and {{extra}}\n", result);
    }

    // DoesNotContain loop is the belt-and-suspenders proof: each declared token that received a value must leave
    // zero literal survivors in the output, independent of the Equal assertion.
    [Theory]
    [MemberData(nameof(TokenSubstitutionCases))]
    public void Render_WhenBodyContainsMixedTokens_OnlyDeclaredNamesAreSubstituted(RenderCase c)
    {
        var result = _renderer.Render(c.Template, Definition(c.FieldNames), c.Values);

        Assert.Equal(c.Expected, result);
        foreach (var name in c.FieldNames.Where(n => c.Values.ContainsKey(n)))
            Assert.DoesNotContain($"{{{{{name}}}}}", result);
    }

    // Phase 2 — §2.2: Odd-bracing grammar boundaries (optional / cut-first set).
    // Pins the documented grammar for edge inputs; unrelated to business logic.
    [Theory]
    [InlineData("{{  name  }}\n", "name", "Trimmed", "Trimmed\n")]
    [InlineData("{{{name}}}\n", "name", "X", "{{{name}}}\n")]
    [InlineData("no closing {{name\n", "name", "X", "no closing {{name\n")]
    [InlineData("{{a}}{{b}}\n", "a", "1", "1{{b}}\n")]
    public void Render_WhenOddBracingGrammarBoundary_AppliesDocumentedBehavior(
        string template, string fieldName, string value, string expected)
    {
        var values = new Dictionary<string, string> { [fieldName] = value };
        var result = _renderer.Render(template, Definition(fieldName), values);
        Assert.Equal(expected, result);
    }
}
