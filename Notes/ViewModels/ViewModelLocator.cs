using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Notes.Core.ViewModels;

namespace Notes.ViewModels;

public sealed class ViewModelLocator
{
    public MainWindowViewModel? Main => Resolve<MainWindowViewModel>();

    public NoteTreeViewModel? Tree => Resolve<NoteTreeViewModel>();

    public NoteEditorViewModel? Editor => Resolve<NoteEditorViewModel>();

    public NoteSearchViewModel? Search => Resolve<NoteSearchViewModel>();

    public TemplateFormViewModel? Form => Resolve<TemplateFormViewModel>();

    public TemplatePickerViewModel? Picker => Resolve<TemplatePickerViewModel>();

    private static T? Resolve<T>() where T : class =>
        Design.IsDesignMode ? null : App.Services.GetRequiredService<T>();
}
