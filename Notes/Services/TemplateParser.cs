using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Notes.Models;
using YamlDotNet.RepresentationModel;

namespace Notes.Services;

/// <summary>
/// Parses a template's <c>form</c> frontmatter map into a <see cref="FormDefinition"/>.
/// Mirrors <see cref="NoteMetadataParser"/>: a shared Markdig <c>UseYamlFrontMatter()</c>
/// pipeline extracts the frontmatter block, then the block is walked with YamlDotNet's
/// representation model so field order follows template document order (a plain
/// <c>Dictionary&lt;,&gt;</c> deserialization would not guarantee ordering). Any failure or
/// absent/malformed <c>form</c> map collapses to <see cref="FormDefinition.Empty"/> via the
/// deliberate broad catch (see context/foundation/lessons.md — do not narrow).
/// </summary>
public sealed class TemplateParser : ITemplateParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
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
            var stream = new YamlStream();
            stream.Load(new StringReader(yamlBlock.Lines.ToString()));
            if (stream.Documents.Count == 0 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return FormDefinition.Empty;
            }

            if (Child(root, "form") is not YamlMappingNode formMap)
            {
                return FormDefinition.Empty;
            }

            var fields = new List<FormFieldEntry>();
            foreach (var (keyNode, valueNode) in formMap.Children)
            {
                if (keyNode is not YamlScalarNode { Value: { } name } ||
                    valueNode is not YamlMappingNode fieldMap)
                {
                    continue;
                }

                fields.Add(new FormFieldEntry(name, ReadField(fieldMap)));
            }

            return new FormDefinition(fields);
        }
        catch (Exception)
        {
            return FormDefinition.Empty;
        }
    }

    private static FormField ReadField(YamlMappingNode map)
    {
        var type = Scalar(map, "type") ?? string.Empty;
        var label = Scalar(map, "label") ?? string.Empty;
        var format = Scalar(map, "format");

        IReadOnlyList<string>? entries = null;
        if (Child(map, "entries") is YamlSequenceNode sequence)
        {
            entries = sequence.Children
                .OfType<YamlScalarNode>()
                .Select(n => n.Value ?? string.Empty)
                .ToList();
        }

        return new FormField(type, label, entries, format);
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children
            .FirstOrDefault(kv => kv.Key is YamlScalarNode { Value: { } v } && v == key)
            .Value;

    private static string? Scalar(YamlMappingNode map, string key) =>
        Child(map, key) is YamlScalarNode scalar ? scalar.Value : null;
}
