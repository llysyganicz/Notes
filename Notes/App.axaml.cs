using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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
