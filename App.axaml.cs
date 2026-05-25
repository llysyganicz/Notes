using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Notes.Models;
using Notes.Services;
using Notes.ViewModels;

namespace Notes;

public partial class App : Application
{
    public static IServiceProvider Services { get; set; } = null!;

    public Window? MainWindow =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Services.GetRequiredService<MainWindow>();
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            window.Show();
            Start(desktop, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void Start(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel viewModel)
    {
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var folderPicker = Services.GetRequiredService<IFolderPicker>();

        var settings = settingsService.Load();
        if (!string.IsNullOrEmpty(settings.WorkspacePath) && !Directory.Exists(settings.WorkspacePath))
        {
            settingsService.Save(AppSettings.Empty);
            settings = AppSettings.Empty;
        }

        if (string.IsNullOrEmpty(settings.WorkspacePath))
        {
            var picked = await folderPicker.PickFolder();
            if (picked is null)
            {
                desktop.Shutdown(0);
                return;
            }

            settingsService.Save(new AppSettings(picked));
            viewModel.WorkspacePath = picked;
        }
        else
        {
            viewModel.WorkspacePath = settings.WorkspacePath;
        }
    }
}
