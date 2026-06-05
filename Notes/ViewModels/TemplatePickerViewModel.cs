using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notes.Models;

namespace Notes.ViewModels;

/// <summary>
/// Backs the template picker. Resolved per invocation (DI transient) and bound to the dialog's
/// DataContext via the locator: <see cref="Load"/> populates the template list and resets the
/// result. Submit/cancel own the result; <see cref="CloseRequested"/> lets the host close its
/// window without the VM referencing the view. Mirrors <see cref="TemplateFormViewModel"/>.
/// </summary>
public sealed partial class TemplatePickerViewModel : ObservableObject
{
    /// <summary>Raised when the user submits or cancels; the host closes the dialog.</summary>
    public event Action? CloseRequested;

    /// <summary>The available templates, in catalog order.</summary>
    public ObservableCollection<TemplateInfo> Templates { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private TemplateInfo? _selectedTemplate;

    /// <summary>The chosen template after <see cref="Submit"/>, or <c>null</c> while unsubmitted or cancelled.</summary>
    public TemplateInfo? Result { get; private set; }

    /// <summary>(Re)populates the picker for a fresh invocation and pre-selects the first template.</summary>
    public void Load(IReadOnlyList<TemplateInfo> templates)
    {
        Result = null;
        Templates.Clear();
        foreach (var template in templates)
        {
            Templates.Add(template);
        }

        SelectedTemplate = Templates.Count > 0 ? Templates[0] : null;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void Submit()
    {
        Result = SelectedTemplate;
        CloseRequested?.Invoke();
    }

    private bool CanSubmit() => SelectedTemplate is not null;

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke();
    }
}
