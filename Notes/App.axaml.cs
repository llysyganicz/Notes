using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Notes.Services;
using Notes.Core.Services;
using Notes.ViewModels;
using Notes.Core.ViewModels;

namespace Notes;

public partial class App : Application
{
    public static IServiceProvider Services { get; set; } = null!;

    private Window? _mainWindow;

    public virtual Window? MainWindow
    {
        get => _mainWindow ?? (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        set => _mainWindow = value;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = Services.GetRequiredService<INoteSearchIndex>();
            _ = Services.GetRequiredService<OrphanedTempCleaner>();
            var window = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = window;
            window.Show();
            _ = StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var viewModel = Services.GetRequiredService<MainWindowViewModel>();
        var ready = await viewModel.InitializeAsync();
        if (!ready)
        {
            desktop.Shutdown(0);
        }
    }
}
