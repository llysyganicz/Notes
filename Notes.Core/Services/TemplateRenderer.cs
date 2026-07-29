using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Notes.Core.Models;

namespace Notes.Core.Services;

/// <summary>
/// The pure heart of note-from-template. Splits the frontmatter region from the body,
/// textually strips the <c>form</c> block from the frontmatter (dropping the whole
/// <c>---</c> fence if no keys remain), then substitutes declared <c>{{field}}</c>
/// tokens in the body only. Other frontmatter keys pass through verbatim — no YAML
/// deserialize/reserialize round-trip that would reorder, restyle, or drop comments.
/// Each line carries its original terminator (<c>\n</c> or <c>\r\n</c>) through the whole
/// pipeline, so a CRLF template emerges as CRLF, not a mixed-ending file.
/// </summary>
public sealed class TemplateRenderer : ITemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    /// <summary>One source line: its content (sans terminator) and the terminator that followed it.</summary>
    private readonly record struct Line(string Content, string Ending);

    public string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        templateText ??= string.Empty;

        var lines = SplitLines(templateText);

        // No leading `---` fence → the whole text is body.
        if (lines.Count == 0 || lines[0].Content != "---")
        {
            return SubstituteBody(templateText, definition, values);
        }

        var closing = FindClosingFence(lines);

        // No closing fence → not a frontmatter block; treat the whole text as body.
        if (closing < 0)
        {
            return SubstituteBody(templateText, definition, values);
        }

        var keptFrontmatter = StripFormBlock(lines.GetRange(1, closing - 1));
        var body = SubstituteBody(Join(lines, closing + 1, lines.Count), definition, values);

        if (keptFrontmatter.Count == 0)
        {
            // The form block was the only key — drop the fence entirely.
            return body;
        }

        var builder = new StringBuilder();
        Append(builder, lines[0]);                  // opening fence, verbatim
        foreach (var line in keptFrontmatter)
        {
            Append(builder, line);
        }
        Append(builder, lines[closing]);            // closing fence, verbatim
        builder.Append(body);
        return builder.ToString();
    }

    public string RenderBody(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        templateText ??= string.Empty;
        var lines = SplitLines(templateText);

        if (lines.Count == 0 || lines[0].Content != "---")
        {
            return SubstituteBody(templateText, definition, values);
        }

        var closing = FindClosingFence(lines);
        if (closing < 0)
        {
            return SubstituteBody(templateText, definition, values);
        }

        var body = Join(lines, closing + 1, lines.Count);
        return SubstituteBody(body, definition, values);
    }

    private static int FindClosingFence(List<Line> lines)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Content == "---")
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits text into lines, keeping each line's terminator so that concatenating every
    /// <see cref="Line.Content"/> + <see cref="Line.Ending"/> reproduces the input exactly.
    /// </summary>
    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var cursor = 0;
        while (cursor < text.Length)
        {
            var newline = text.IndexOf('\n', cursor);
            if (newline < 0)
            {
                lines.Add(new Line(text.Substring(cursor), string.Empty));
                break;
            }

            var contentEnd = newline;
            var ending = "\n";
            if (contentEnd > cursor && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
                ending = "\r\n";
            }

            lines.Add(new Line(text.Substring(cursor, contentEnd - cursor), ending));
            cursor = newline + 1;
        }

        return lines;
    }

    /// <summary>
    /// Removes the <c>form:</c> line and its more-indented continuation lines from the
    /// frontmatter, returning the surviving lines verbatim (terminators intact).
    /// </summary>
    private static List<Line> StripFormBlock(List<Line> lines)
    {
        var kept = new List<Line>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            if (IsFormKeyLine(lines[i].Content))
            {
                // Skip the form: line plus every following indented continuation line,
                // stopping at the next top-level key (column 0) or the end of the block.
                // Blank lines inside the block are continuations, not terminators —
                // otherwise a blank between fields would leak the rest of the block.
                i++;
                while (i < lines.Count && (IsIndented(lines[i].Content) || string.IsNullOrWhiteSpace(lines[i].Content)))
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

    private static bool IsFormKeyLine(string content) =>
        content.StartsWith("form:");

    private static bool IsIndented(string content) =>
        content.Length > 0 && (content[0] == ' ' || content[0] == '\t');

    private static string Join(List<Line> lines, int start, int end)
    {
        var builder = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            Append(builder, lines[i]);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, Line line) =>
        builder.Append(line.Content).Append(line.Ending);

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
