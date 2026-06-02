using Avalonia;
using Avalonia.Headless;
using Notes.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Notes.Tests;

public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
