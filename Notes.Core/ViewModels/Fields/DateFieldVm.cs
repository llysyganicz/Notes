using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Notes.Core.ViewModels.Fields;

/// <summary>
/// A date field backed by a nullable <see cref="DateTimeOffset"/> (the type Avalonia's
/// <c>DatePicker.SelectedDate</c> binds to), starting empty. <see cref="RenderValue"/>
/// emits ISO <c>yyyy-MM-dd</c> by default, or the field's <see cref="Format"/> when set;
/// an empty field renders as the empty string.
/// </summary>
public sealed partial class DateFieldVm : FieldVm
{
    private const string IsoDate = "yyyy-MM-dd";

    [ObservableProperty]
    private DateTimeOffset? _value;

    public DateFieldVm(string name, string label, string? format = null)
        : base(name, label)
    {
        Format = format;
    }

    /// <summary>Optional .NET date format string (e.g. <c>"dd/MM/yyyy"</c>).</summary>
    public string? Format { get; }

    public override string RenderValue()
    {
        if (Value is not { } value)
        {
            return string.Empty;
        }

        var format = string.IsNullOrEmpty(Format) ? IsoDate : Format;
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
