using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Notes.ViewModels;

public sealed class ViewModelLocator
{
    public MainWindowViewModel? Main => Resolve<MainWindowViewModel>();

    public NoteTreeViewModel? Tree => Resolve<NoteTreeViewModel>();

    public NoteEditorViewModel? Editor => Resolve<NoteEditorViewModel>();

    private static T? Resolve<T>() where T : class =>
        Design.IsDesignMode ? null : App.Services.GetRequiredService<T>();
}
