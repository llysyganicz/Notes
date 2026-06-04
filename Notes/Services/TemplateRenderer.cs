using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Notes.Models;

namespace Notes.Services;

/// <summary>
/// The pure heart of note-from-template. Splits the frontmatter region from the body,
/// textually strips the <c>form</c> block from the frontmatter (dropping the whole
/// <c>---</c> fence if no keys remain), then substitutes declared <c>{{field}}</c>
/// tokens in the body only. Other frontmatter keys pass through verbatim — no YAML
/// deserialize/reserialize round-trip that would reorder, restyle, or drop comments.
/// </summary>
public sealed class TemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    public string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        templateText ??= string.Empty;

        if (!TrySplitFrontmatter(templateText, out var inner, out var body))
        {
            // No frontmatter fence — the whole text is body.
            return SubstituteBody(templateText, definition, values);
        }

        var keptLines = StripFormBlock(inner);
        var substitutedBody = SubstituteBody(body, definition, values);

        if (keptLines.Count == 0)
        {
            // The form block was the only key — drop the fence entirely.
            return substitutedBody;
        }

        var builder = new StringBuilder();
        builder.Append("---\n");
        foreach (var line in keptLines)
        {
            builder.Append(line).Append('\n');
        }
        builder.Append("---\n");
        builder.Append(substitutedBody);
        return builder.ToString();
    }

    /// <summary>
    /// Splits a leading <c>---</c>…<c>---</c> frontmatter fence from the body.
    /// <paramref name="inner"/> receives the lines between the fences (no trailing newline);
    /// <paramref name="body"/> receives everything after the closing fence line.
    /// </summary>
    private static bool TrySplitFrontmatter(string text, out string inner, out string body)
    {
        inner = string.Empty;
        body = text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0 || text.Substring(0, firstNewline).TrimEnd('\r') != "---")
        {
            return false;
        }

        var innerStart = firstNewline + 1;
        var cursor = innerStart;
        while (cursor < text.Length)
        {
            var newline = text.IndexOf('\n', cursor);
            var lineEnd = newline < 0 ? text.Length : newline;
            var line = text.Substring(cursor, lineEnd - cursor).TrimEnd('\r');
            if (line == "---")
            {
                inner = text.Substring(innerStart, cursor - innerStart).TrimEnd('\r', '\n');
                body = newline < 0 ? string.Empty : text.Substring(newline + 1);
                return true;
            }

            if (newline < 0)
            {
                break;
            }

            cursor = newline + 1;
        }

        return false;
    }

    /// <summary>
    /// Removes the <c>form:</c> line and its more-indented continuation lines from the
    /// frontmatter, returning the surviving lines verbatim.
    /// </summary>
    private static List<string> StripFormBlock(string inner)
    {
        var lines = inner.Split('\n');
        var kept = new List<string>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (IsFormKeyLine(lines[i]))
            {
                // Skip the form: line plus every following indented continuation line,
                // stopping at the next top-level key (column 0) or the end of the block.
                i++;
                while (i < lines.Length && IsIndented(lines[i]))
                {
                    i++;
                }

                i--; // the for-loop's i++ re-examines the stopper line.
                continue;
            }

            kept.Add(lines[i]);
        }

        return kept;
    }

    private static bool IsFormKeyLine(string line) =>
        line.TrimEnd('\r').StartsWith("form:");

    private static bool IsIndented(string line) =>
        line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

    private static string SubstituteBody(string body, FormDefinition definition, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex.Replace(body, match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (!definition.Names.Contains(name))
            {
                // Undeclared token — leave it exactly as written.
                return match.Value;
            }

            return values.TryGetValue(name, out var value) && value is not null
                ? value
                : string.Empty;
        });
}
