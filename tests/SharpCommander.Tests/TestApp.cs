using Avalonia;
using Avalonia.Headless;
using SharpCommander.Desktop;

[assembly: AvaloniaTestApplication(typeof(SharpCommander.Tests.TestAppBuilder))]

namespace SharpCommander.Tests;

/// <summary>
/// Boots the real <see cref="App"/> (styles, themes, resources) on the headless platform so UI tests
/// exercise the same XAML the users see. Rendering goes through Skia (instead of the headless no-op drawing)
/// so <c>CaptureRenderedFrame</c> yields real pixels for the screenshot generator; no window is created by
/// the App itself in this mode.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia()
        .WithInterFont();
}
