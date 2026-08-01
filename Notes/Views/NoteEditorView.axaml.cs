using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Notes.Services;
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
        ApplyGruvboxSyntaxHighlighting();
        Editor.TextChanged += OnEditorTextChanged;
        ApplyGruvboxMarkdownPreviewStyle();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        // The markdown preview (Notes/Themes/GruvboxMarkdownPreview.axaml) is
        // DynamicResource-based and re-resolves colors on its own. The editor's
        // syntax-highlighting definition is not - .xshd <Color> values are
        // load-time literals - so it must be explicitly reloaded and reassigned
        // when the OS theme toggles.
        ApplyGruvboxSyntaxHighlighting();
    }

    private void ApplyGruvboxSyntaxHighlighting()
    {
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        Editor.SyntaxHighlighting = GruvboxHighlightingLoader.Load(variant);
    }

    private void ApplyGruvboxMarkdownPreviewStyle()
    {
        // MarkdownScrollViewer.MarkdownStyle can't be set as a XAML attribute
        // (see the comment on <md:MarkdownScrollViewer> in NoteEditorView.axaml)
        // so it's assigned here via the ordinary CLR property setter instead.
        if (Application.Current?.Resources.TryGetResource("GruvboxMarkdownPreview", ThemeVariant.Default, out var resource) == true
            && resource is IStyle style)
        {
            Preview.MarkdownStyle = style;
        }
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
