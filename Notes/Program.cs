using System;
using System.IO.Abstractions;
using Avalonia;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Notes.Services;
using Notes.ViewModels;
using Notes.Views;

namespace Notes;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        App.Services = BuildServiceProvider();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPathGuard, PathGuard>();
        services.AddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        services.AddSingleton<NoteTreeBuilder>();
        services.AddSingleton<INoteDeleter, NoteDeleter>();
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();
        services.AddSingleton<INoteFileService, NoteFileService>();
        services.AddSingleton<INoteMetadataParser, NoteMetadataParser>();
        services.AddSingleton<INoteSearchIndex, NoteSearchIndex>();
        services.AddSingleton<IAutoSaveScheduler, AutoSaveScheduler>();
        services.AddSingleton<INameValidator, NameValidator>();
        services.AddSingleton<INoteFolderService, NoteFolderService>();
        services.AddSingleton<INewNoteDialogService, NewNoteDialogService>();
        services.AddSingleton<ITemplateParser, TemplateParser>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<ITemplateCatalog, TemplateCatalog>();
        services.AddSingleton<ITemplatePickerDialogService, TemplatePickerDialogService>();
        services.AddSingleton<ITemplateFormDialogService, TemplateFormDialogService>();
        services.AddSingleton<OrphanedTempCleaner>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<NoteTreeViewModel>();
        services.AddSingleton<NoteEditorViewModel>();
        services.AddSingleton<NoteSearchViewModel>();

        services.AddTransient<MainWindow>();
        services.AddTransient<TemplateFormViewModel>();
        services.AddTransient<TemplateFormDialog>();
        services.AddTransient<TemplatePickerViewModel>();
        services.AddTransient<TemplatePickerDialog>();

        return services.BuildServiceProvider();
    }
}
