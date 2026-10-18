using Avalonia;
using Avalonia.Headless;
using LegoToyPad.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LegoToyPad.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
