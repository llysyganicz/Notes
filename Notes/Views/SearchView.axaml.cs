using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Notes.Models;
using Notes.ViewModels;

namespace Notes.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();

        // Avalonia paints the overlay only when IsVisible flips true; we focus the
        // query box on that edge so Ctrl+F lands keystrokes in the input directly.
        PropertyChanged += OnPropertyChanged;
        ResultsList.Tapped += OnResultsListTapped;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is true)
        {
            Dispatcher.UIThread.Post(() => QueryBox.Focus());
        }
    }

    private void OnResultsListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not NoteSearchViewModel vm)
        {
            return;
        }

        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is NoteSearchResult result)
        {
            vm.OpenResultCommand.Execute(result);
        }
    }
}
