using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Extensions.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Notes.Models;

namespace Notes.Services;

/// <summary>
/// Parses a template's <c>form</c> frontmatter map into a <see cref="FormDefinition"/>.
/// Mirrors <see cref="NoteMetadataParser"/>: a shared Markdig <c>UseYamlFrontMatter()</c>
/// pipeline extracts the frontmatter block, then a YamlDotNet <see cref="IDeserializer"/>
/// deserializes it into a typed shape whose <c>form</c> map is the field definition. The
/// deserializer reads the mapping in document order and the backing dictionary preserves
/// insertion order, so field order follows the template. Any failure or absent/malformed
/// <c>form</c> map collapses to <see cref="FormDefinition.Empty"/> via the deliberate broad
/// catch (see context/foundation/lessons.md — do not narrow).
/// </summary>
public sealed class TemplateParser : ITemplateParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public FormDefinition Parse(string? templateText)
    {
        if (string.IsNullOrEmpty(templateText))
        {
            return FormDefinition.Empty;
        }

        var yamlBlock = Markdig.Markdown.Parse(templateText, Pipeline)
            .OfType<YamlFrontMatterBlock>()
            .FirstOrDefault();
        if (yamlBlock is null)
        {
            return FormDefinition.Empty;
        }

        try
        {
            var shape = YamlDeserializer.Deserialize<FrontmatterShape?>(yamlBlock.Lines.ToString());
            if (shape?.Form is not { Count: > 0 } form)
            {
                return FormDefinition.Empty;
            }

            var fields = form
                .Select(kv => new FormFieldEntry(
                    kv.Key,
                    new FormField(
                        kv.Value?.Type ?? string.Empty,
                        kv.Value?.Label ?? string.Empty,
                        kv.Value?.Entries,
                        kv.Value?.Format)))
                .ToList();

            return new FormDefinition(fields);
        }
        catch (Exception)
        {
            return FormDefinition.Empty;
        }
    }

    private sealed class FrontmatterShape
    {
        public Dictionary<string, FieldShape?>? Form { get; set; }
    }

    private sealed class FieldShape
    {
        public string? Type { get; set; }
        public string? Label { get; set; }
        public List<string>? Entries { get; set; }
        public string? Format { get; set; }
    }
}
