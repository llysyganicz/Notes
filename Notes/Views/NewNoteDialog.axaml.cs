using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Notes.Views;

public partial class NewNoteDialog : Window
{
    private Func<string, string?>? _validate;
    private string? _result;

    public NewNoteDialog()
    {
        InitializeComponent();
        CreateButton.Click += OnCreate;
        CancelButton.Click += OnCancel;
        NameInput.TextChanged += OnTextChanged;
    }

    public static async Task<string?> Show(
        Window owner,
        string parentFolderDisplay,
        Func<string, string?> validate)
    {
        var dialog = new NewNoteDialog
        {
            _validate = validate,
        };
        dialog.ParentText.Text = $"Creating in: {parentFolderDisplay}";
        dialog.RefreshValidation();
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) => RefreshValidation();

    private void RefreshValidation()
    {
        var text = NameInput.Text ?? string.Empty;
        var error = _validate?.Invoke(text);
        if (error is null)
        {
            ErrorText.IsVisible = false;
            ErrorText.Text = string.Empty;
            CreateButton.IsEnabled = true;
        }
        else
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            CreateButton.IsEnabled = false;
        }
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        var text = NameInput.Text ?? string.Empty;
        if (_validate?.Invoke(text) is not null)
        {
            return;
        }

        _result = text;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }
}
