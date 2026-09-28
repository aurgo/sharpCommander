using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont();

        // The rendering subsystem is the first thing each test's setup starts, before anything has asked for the
        // services TestIsolation clears.
        var skia = builder.RenderingSubsystemInitializer!;
        return builder.UseRenderingSubsystem(() =>
        {
            TestIsolation.StartClean();
            skia();
        }, builder.RenderingSubsystemName!);
    }
}

/// <summary>
/// Makes every UI test start from the Avalonia state of a fresh application, whatever earlier tests left behind.
/// </summary>
/// <remarks>
/// The headless session gives each test a service-locator scope of its own and disposes it afterwards. A few
/// services are created on first use and registered in whichever scope is current at that moment: the media
/// context that drives layout and rendering, the render loop and the font manager. The locator is one static for
/// every thread, so when something asks for one of them between two tests (a background thread finishing an
/// operation, say), it is registered in the root scope and every later test inherits it. Bound to a dispatcher
/// that no longer runs, it never lays out, renders or hit-tests again: windows still take keys, but clicks, focus
/// requests and templates stop working for the rest of the run, and once the session disposes a shared font
/// manager, windows cannot even be created. A test whose clean-up breaks off half way leaves its whole scope in
/// the same position. This drops both before each test, so each one creates its own again.
/// The fields it reaches are Avalonia internals; if a later version renames them, this quietly does nothing.
/// </remarks>
internal static class TestIsolation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly PropertyInfo? CurrentScope =
        typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static);

    private static readonly FieldInfo? ParentScope = typeof(AvaloniaLocator).GetField("_parentScope", Private);

    private static readonly FieldInfo? Registry = typeof(AvaloniaLocator).GetField("_registry", Private);

    private static readonly MethodInfo? ResetLoadedQueue =
        typeof(Control).GetMethod("ResetLoadedQueueForUnitTests", BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>The services that each test has to create for itself.</summary>
    private static readonly Type[] PerTestServices = new[]
    {
        typeof(FontManager).Assembly.GetType("Avalonia.Media.MediaContext"),
        typeof(FontManager).Assembly.GetType("Avalonia.Rendering.IRenderLoop"),
        typeof(FontManager)
    }.OfType<Type>().ToArray();

    /// <summary>Registered in a scope, it answers "none" there instead of asking the scopes above.</summary>
    private static readonly Func<object?> None = () => null;

    public static void StartClean()
    {
        if (CurrentScope?.GetValue(null) is not { } scope || ParentScope is null
            || Registry?.GetValue(scope) is not IDictionary registry)
        {
            return;
        }

        // Every test's scope is entered from the root; anything in between is a scope some test never left.
        var root = scope;
        while (ParentScope.GetValue(root) is { } parent)
        {
            root = parent;
        }

        if (ParentScope.GetValue(scope) is { } above && !ReferenceEquals(above, root))
        {
            ParentScope.SetValue(scope, root);
            Console.Error.WriteLine("TestIsolation: an earlier UI test never left its Avalonia scope; it was dropped.");
        }

        // Such a test also leaves its synchronization context installed, posting to its own dispatcher, which is
        // gone. The setup keeps any Avalonia context it finds rather than install one for this test's dispatcher,
        // so this test's awaits would never resume.
        SynchronizationContext.SetSynchronizationContext(null);

        // The first use in this test then creates a new one and registers it here, in place of None.
        foreach (var service in PerTestServices)
        {
            registry[service] = None;
        }

        // Controls queued for Loaded on a dispatcher that is gone would otherwise hold it back for every later one.
        ResetLoadedQueue?.Invoke(null, null);
    }
}
