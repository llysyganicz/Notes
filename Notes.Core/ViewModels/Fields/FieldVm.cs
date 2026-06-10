using CommunityToolkit.Mvvm.ComponentModel;

namespace Notes.Core.ViewModels.Fields;

/// <summary>
/// Base for the per-field-type form view models. Each concrete VM owns a typed,
/// two-way-bound input value and converts it to the string the renderer consumes
/// via <see cref="RenderValue"/>. Every field starts empty (no defaults).
/// </summary>
public abstract class FieldVm : ObservableObject
{
    protected FieldVm(string name, string label)
    {
        Name = name;
        Label = label;
    }

    /// <summary>The placeholder key — matches a <c>{{name}}</c> token in the template body.</summary>
    public string Name { get; }

    /// <summary>The human-facing label shown next to the input in the form.</summary>
    public string Label { get; }

    /// <summary>The formatted string value this field contributes to the substitution map.</summary>
    public abstract string RenderValue();
}
