using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Models;
using Notes.ViewModels.Fields;

namespace Notes.ViewModels;

/// <summary>
/// Backs the dynamic typed form. Resolved per invocation (DI transient) and bound to the
/// dialog's DataContext via the locator: <see cref="Load"/> builds the ordered field-VM
/// collection from a <see cref="FormDefinition"/> and resets the result. Submit/cancel own
/// the result; <see cref="CloseRequested"/> lets the host close its window without the VM
/// referencing the view.
/// </summary>
public sealed partial class TemplateFormViewModel : ObservableObject
{
    /// <summary>Raised when the user submits or cancels; the host closes the dialog.</summary>
    public event Action? CloseRequested;

    /// <summary>One field VM per declared field, in template document order.</summary>
    public ObservableCollection<FieldVm> Fields { get; } = new();

    /// <summary>
    /// The collected <c>name → formatted-string</c> map after <see cref="Submit"/>,
    /// or <c>null</c> while the form is unsubmitted or was cancelled.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Result { get; private set; }

    /// <summary>(Re)populates the form for a fresh invocation from the parsed definition.</summary>
    public void Load(FormDefinition definition)
    {
        Result = null;
        Fields.Clear();
        foreach (var entry in definition.Fields)
        {
            Fields.Add(CreateField(entry));
        }
    }

    [RelayCommand]
    private void Submit()
    {
        Result = Fields.ToDictionary(f => f.Name, f => f.RenderValue(), StringComparer.Ordinal);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke();
    }

    private static FieldVm CreateField(FormFieldEntry entry)
    {
        var field = entry.Field;
        return (field.Type ?? string.Empty).ToLowerInvariant() switch
        {
            "date" => new DateFieldVm(entry.Name, field.Label, field.Format),
            "number" => new NumberFieldVm(entry.Name, field.Label, field.Format),
            "select" => new SelectFieldVm(entry.Name, field.Label, field.Entries ?? Array.Empty<string>()),
            _ => new TextFieldVm(entry.Name, field.Label),
        };
    }
}
