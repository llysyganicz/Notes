using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Notes.Services;
using Notes.ViewModels;

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

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        services.AddSingleton<NoteTreeBuilder>();
        services.AddSingleton<INoteDeleter, NoteDeleter>();
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();

        services.AddTransient<MainWindow>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
