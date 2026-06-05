using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Notes.ViewModels.Fields;

/// <summary>
/// A numeric field backed by a nullable <see cref="decimal"/> starting empty.
/// <see cref="RenderValue"/> formats with <b>invariant culture</b> (a culture-stable
/// document is the goal), applying the field's <see cref="Format"/> when present, else a
/// plain round-trip. The <see cref="ParsingNumberStyle"/>/<see cref="FormatString"/>
/// properties configure the bound <c>NumericUpDown</c> so whole-number-only formats
/// (e.g. <c>"0"</c>, <c>"F0"</c>) reject decimal entry.
/// </summary>
public sealed partial class NumberFieldVm : FieldVm
{
    private static readonly Regex StandardFormatRegex = new(@"^[A-Za-z](\d+)$", RegexOptions.Compiled);

    [ObservableProperty]
    private decimal? _value;

    public NumberFieldVm(string name, string label, string? format = null)
        : base(name, label)
    {
        Format = format;
    }

    /// <summary>Optional .NET numeric format string (e.g. <c>"F2"</c>, <c>"0.##"</c>).</summary>
    public string? Format { get; }

    /// <summary>Passed to the bound <c>NumericUpDown.FormatString</c>.</summary>
    public string? FormatString => Format;

    /// <summary>
    /// <see cref="NumberStyles.Integer"/> when the format admits no decimal places, so the
    /// control rejects fractional input; otherwise <see cref="NumberStyles.Number"/>.
    /// </summary>
    public NumberStyles ParsingNumberStyle =>
        IsIntegerFormat(Format) ? NumberStyles.Integer : NumberStyles.Number;

    public override string RenderValue()
    {
        if (Value is not { } value)
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(Format)
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A format admits only whole numbers when it has no fractional part: a standard
    /// specifier with a zero precision (<c>"F0"</c>, <c>"N0"</c>) or a custom pattern with
    /// no decimal point (<c>"0"</c>, <c>"###"</c>). An absent format allows decimals.
    /// </summary>
    private static bool IsIntegerFormat(string? format)
    {
        if (string.IsNullOrEmpty(format) || format.Contains('.'))
        {
            return false;
        }

        var standard = StandardFormatRegex.Match(format);
        return !standard.Success || int.Parse(standard.Groups[1].Value) == 0;
    }
}
