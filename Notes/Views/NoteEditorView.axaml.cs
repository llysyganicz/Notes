using System;
using System.ComponentModel;
using Avalonia.Controls;
using AvaloniaEdit.Highlighting;
using Notes.ViewModels;
using Notes.Core.ViewModels;

namespace Notes.Views;

public partial class NoteEditorView : UserControl
{
    private NoteEditorViewModel? _viewModel;
    private bool _suppressEvents;

    public NoteEditorView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown");
        Editor.TextChanged += OnEditorTextChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.InsertAtCaretRequested -= OnInsertAtCaretRequested;
        }

        _viewModel = DataContext as NoteEditorViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.InsertAtCaretRequested += OnInsertAtCaretRequested;
            ApplyLoadedText(_viewModel.LoadedText);
        }
    }

    private void OnInsertAtCaretRequested(string body)
    {
        if (_viewModel is null || !_viewModel.IsEditing)
        {
            return;
        }

        var offset = Editor.SelectionStart;
        Editor.Document.Replace(offset, Editor.SelectionLength, body);
        Editor.SelectionLength = 0;
        Editor.CaretOffset = offset + body.Length;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteEditorViewModel.LoadedText) && _viewModel is not null)
        {
            ApplyLoadedText(_viewModel.LoadedText);
        }
    }

    private void ApplyLoadedText(string text)
    {
        if (Editor.Text == text)
        {
            return;
        }

        _suppressEvents = true;
        try
        {
            Editor.Text = text;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents || _viewModel is null)
        {
            return;
        }

        _viewModel.OnEditorTextChanged(Editor.Text);
    }
}
