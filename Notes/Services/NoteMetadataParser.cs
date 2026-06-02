using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Yaml;
using Notes.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Notes.Services;

public sealed class NoteMetadataParser : INoteMetadataParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly Regex CanonicalTagRegex = new(@"\A[a-z0-9-]+\z", RegexOptions.Compiled);

    public NoteMetadata Parse(string? noteText)
    {
        if (string.IsNullOrEmpty(noteText))
        {
            return NoteMetadata.Empty;
        }

        var yamlBlock = Markdig.Markdown.Parse(noteText, Pipeline)
            .OfType<YamlFrontMatterBlock>()
            .FirstOrDefault();
        if (yamlBlock is null)
        {
            return NoteMetadata.Empty;
        }

        try
        {
            var shape = YamlDeserializer.Deserialize<FrontmatterShape?>(yamlBlock.Lines.ToString());
            return new NoteMetadata(NormalizeTags(shape?.Tags));
        }
        catch (Exception)
        {
            return NoteMetadata.Empty;
        }
    }

    private static IReadOnlyList<string> NormalizeTags(List<string?>? raw) =>
        raw is null
            ? Array.Empty<string>()
            : raw
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim().ToLowerInvariant())
                .Where(t => CanonicalTagRegex.IsMatch(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private sealed class FrontmatterShape
    {
        public List<string?>? Tags { get; set; }
    }
}
