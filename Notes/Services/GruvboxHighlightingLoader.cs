using System;
using System.IO;
using System.Xml;
using Avalonia.Platform;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Notes.Services;

/// <summary>
/// Loads the gruvbox-recolored markdown syntax-highlighting definition
/// (<c>avares://Notes/Themes/GruvboxMarkdownHighlighting.xshd</c>), substituting
/// the six <c>__TOKEN__</c> placeholders in that template with the hex colors
/// matching the active <see cref="ThemeVariant"/> before parsing - .xshd
/// &lt;Color&gt; values are load-time literals with no DynamicResource
/// equivalent, so unlike the rest of the app's gruvbox theming this can't
/// re-resolve on its own; callers re-invoke <see cref="Load"/> with the new
/// variant and reassign <c>TextEditor.SyntaxHighlighting</c> when the OS
/// theme toggles (see NoteEditorView.axaml.cs).
/// </summary>
public static class GruvboxHighlightingLoader
{
    private const string ResourceUri = "avares://Notes/Themes/GruvboxMarkdownHighlighting.xshd";

    // Mirrors Notes/Themes/GruvboxPalette.axaml's "Bright*" (dark) / plain (light)
    // accent ramps so the editor and the rest of the gruvbox chrome stay cohesive.
    private static readonly (string Heading, string Emphasis, string Strong, string Code, string BlockQuote, string Link, string Image, string LineBreakBg) DarkTokens =
        ("#FE8019", "#FABD2F", "#FABD2F", "#8EC07C", "#928374", "#83A598", "#B8BB26", "#3C3836");

    private static readonly (string Heading, string Emphasis, string Strong, string Code, string BlockQuote, string Link, string Image, string LineBreakBg) LightTokens =
        ("#AF3A03", "#B57614", "#B57614", "#427B58", "#928374", "#076678", "#79740E", "#EBDBB2");

    public static IHighlightingDefinition Load(ThemeVariant variant)
    {
        var tokens = variant == ThemeVariant.Light ? LightTokens : DarkTokens;

        string template;
        using (var stream = AssetLoader.Open(new Uri(ResourceUri)))
        using (var reader = new StreamReader(stream))
        {
            template = reader.ReadToEnd();
        }

        var xshd = template
            .Replace("__HEADING__", tokens.Heading)
            .Replace("__EMPHASIS__", tokens.Emphasis)
            .Replace("__STRONG__", tokens.Strong)
            .Replace("__CODE__", tokens.Code)
            .Replace("__BLOCKQUOTE__", tokens.BlockQuote)
            .Replace("__LINK__", tokens.Link)
            .Replace("__IMAGE__", tokens.Image)
            .Replace("__LINEBREAK_BG__", tokens.LineBreakBg);

        using var stringReader = new StringReader(xshd);
        using var xmlReader = XmlReader.Create(stringReader);
        return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
    }
}
