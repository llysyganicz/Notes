using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Notes.ViewModels.Fields;

/// <summary>
/// A dropdown field. Exposes the declared <see cref="Entries"/> for the bound
/// <c>ComboBox</c>; an unselected field renders as the empty string.
/// </summary>
public sealed partial class SelectFieldVm : FieldVm
{
    [ObservableProperty]
    private string? _selectedEntry;

    public SelectFieldVm(string name, string label, IReadOnlyList<string> entries)
        : base(name, label)
    {
        Entries = entries;
    }

    public IReadOnlyList<string> Entries { get; }

    public override string RenderValue() => SelectedEntry ?? string.Empty;
}
