using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SharpCommander.Core.Interfaces;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;

namespace SharpCommander.Desktop;

/// <summary>
/// Main application class for SharpCommander: builds the object graph, installs the UI-thread exception
/// handler and makes sure pending settings are written before the process exits.
/// </summary>
public sealed class App : Application
{
    private bool _showingUnhandledError;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Avalonia's DataValidators is safe to use with compiled bindings")]
    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid duplicate validation plugins - safe with compiled bindings
        if (BindingPlugins.DataValidators.Count > 0)
        {
            BindingPlugins.DataValidators.RemoveAt(0);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Every path in the application goes through the router: local ones reach the disk, "sftp://" ones
            // reach an open session, and nothing above this line has to know the difference.
            var connections = new SftpConnections();
            var fileSystemService = new RoutingFileSystemService(new FileSystemService(), connections);
            var settingsService = new SettingsService();
            var dialogService = new DialogService(fileSystemService, settingsService);
            var trashService = new TrashService();
            var clipboardService = new ClipboardService();
            var operationsService = new FileOperationsService(fileSystemService, dialogService, trashService);
            var themeService = new ThemeService();
            var archiveService = new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService());
            var directoryComparer = new DirectoryComparer();
            var undoService = new UndoService();
            var updateService = new GitHubUpdateService();
            var spaceAnalyzer = new SpaceAnalyzerService();

            var mainViewModel = new MainWindowViewModel(
                fileSystemService,
                settingsService,
                dialogService,
                clipboardService,
                operationsService,
                trashService,
                themeService,
                archiveService,
                directoryComparer,
                undoService,
                connections,
                updateService,
                spaceAnalyzer);

            RegisterDispatcherExceptionHandler(dialogService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            // The window awaits ShutdownAsync when it closes; this is the safety net for Cmd+Q and the like:
            // SettingsService never resumes on the UI thread, so blocking here cannot deadlock.
            desktop.ShutdownRequested += (_, _) => SaveStateBlocking(mainViewModel);
            desktop.Exit += (_, _) => mainViewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Exceptions escaping a dispatcher callback are logged and shown instead of terminating the process (H3).
    /// One dialog is shown at a time: exceptions raised while it is open are only logged. The returned token
    /// removes the handler again (tests install it against a fake dialog service).
    /// </summary>
    internal IDisposable RegisterDispatcherExceptionHandler(IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(dialogService);

        DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
            e.Handled = true;

            if (_showingUnhandledError)
            {
                return;
            }

            _showingUnhandledError = true;
            var exception = e.Exception;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await dialogService.ShowErrorAsync("Unexpected error", exception.Message, exception.ToString());
                }
                catch (Exception ex)
                {
                    AppLog.Error("The error dialog itself failed.", ex);
                }
                finally
                {
                    _showingUnhandledError = false;
                }
            });
        };

        Dispatcher.UIThread.UnhandledException += handler;
        return new HandlerRegistration(() => Dispatcher.UIThread.UnhandledException -= handler);
    }

    /// <summary>Runs an action once when disposed.</summary>
    private sealed class HandlerRegistration(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    private static void SaveStateBlocking(MainWindowViewModel viewModel)
    {
        try
        {
            viewModel.SaveStateAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be saved on shutdown.", ex);
        }
    }
}
