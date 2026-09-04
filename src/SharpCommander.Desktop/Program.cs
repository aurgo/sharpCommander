using Avalonia;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Desktop;

internal static class Program
{
    /// <summary>
    /// Main entry point for the application. Logging and the process-wide exception handlers are set up
    /// before Avalonia starts so that nothing can fail silently; the dispatcher handler lives in <see cref="App"/>.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Initialize();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal error: the application terminated unexpectedly.", ex);
            throw;
        }
    }

    /// <summary>
    /// Builds and configures the Avalonia application.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var message = e.IsTerminating ? "Unhandled exception, the process is terminating." : "Unhandled exception.";
        AppLog.Error(message, e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
