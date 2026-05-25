using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Notes.Views;

public partial class ConfirmDialog : Window
{
    private bool _result;

    public ConfirmDialog()
    {
        InitializeComponent();
        YesButton.Click += OnYes;
        NoButton.Click += OnNo;
    }

    public static async Task<bool> Show(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog
        {
            Title = title,
        };
        dialog.MessageText.Text = message;
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnYes(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnNo(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
