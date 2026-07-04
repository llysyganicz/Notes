using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Notes.E2ETests.TestApp))]

namespace Notes.E2ETests;

public sealed class TestApp : App
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    private Window? _mainWindow;

    public override Window? MainWindow
    {
        get => _mainWindow ?? base.MainWindow;
        set => _mainWindow = value;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid auto-showing MainWindow; tests configure per-test services and show the window themselves.
    }
}
