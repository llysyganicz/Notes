using CommunityToolkit.Mvvm.ComponentModel;

namespace Notes.Core.ViewModels.Fields;

/// <summary>A plain free-text field; <see cref="RenderValue"/> is the entered text verbatim.</summary>
public sealed partial class TextFieldVm : FieldVm
{
    [ObservableProperty]
    private string _value = string.Empty;

    public TextFieldVm(string name, string label)
        : base(name, label)
    {
    }

    public override string RenderValue() => Value ?? string.Empty;
}
