using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Opens the application's dialog windows. Every dialog runs on the UI thread and is owned by the active
/// window (or the main window). Without an owner, as in headless tests, nothing is shown and the safe
/// default is returned: false, Cancel, Skip or null.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly Func<Window?> _ownerProvider;

    /// <summary>Where the saved SFTP servers live; null in the dialog tests, which never open that window.</summary>
    private readonly ISettingsService? _settingsService;

    public DialogService(IFileSystemService fileSystemService, ISettingsService settingsService)
        : this(fileSystemService, FindActiveWindow, settingsService)
    {
    }

    /// <summary>Creates a service whose dialogs are owned by the window returned by <paramref name="ownerProvider"/>.</summary>
    internal DialogService(IFileSystemService fileSystemService, Func<Window?> ownerProvider, ISettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(ownerProvider);
        _fileSystemService = fileSystemService;
        _ownerProvider = ownerProvider;
        _settingsService = settingsService;
    }

    public Task ShowHashDialogAsync(string filePath) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var viewModel = new HashViewModel(filePath);
        var window = new HashWindow { DataContext = viewModel };
        window.Closing += (_, _) => viewModel.Cancel();

        // The calculation starts once the window is visible; closing it cancels the calculation.
        await Task.WhenAll(window.ShowDialog(owner), viewModel.StartAsync());
    });

    public Task<FileSystemEntry?> ShowAdvancedSearchDialogAsync(string initialPath) => OnUiThreadAsync<FileSystemEntry?>(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return null;
        }

        var viewModel = new SearchViewModel(_fileSystemService, initialPath);
        var window = new SearchWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        window.Closing += (_, _) => viewModel.Cancel();

        await window.ShowDialog(owner);
        return viewModel.ActivatedResult;
    });

    public Task<bool> ShowMassRenameDialogAsync(IReadOnlyList<string> paths) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return false;
        }

        var viewModel = new MassRenameViewModel(_fileSystemService, paths);
        var window = new MassRenameWindow { DataContext = viewModel };

        await window.ShowDialog(owner);
        return viewModel.HasRenamed;
    });

    public Task ShowPropertiesDialogAsync(FileSystemEntry entry) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var viewModel = new PropertiesViewModel(entry, _fileSystemService);
        var window = new PropertiesWindow { DataContext = viewModel };
        window.Closing += (_, _) => viewModel.Cancel();

        await Task.WhenAll(window.ShowDialog(owner), viewModel.StartAsync());
    });

    public Task ShowViewerAsync(string filePath) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var viewModel = new ViewerViewModel(filePath);
        var window = new ViewerWindow { DataContext = viewModel };
        window.Closing += (_, _) => viewModel.Cancel();

        // The viewer is modeless so several files can stay open; the call returns once the file is loaded.
        window.Show(owner);
        await viewModel.LoadAsync();
    });

    public Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue = "", Func<string, string?>? validate = null) => OnUiThreadAsync<string?>(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return null;
        }

        var dialog = new InputDialog(title, prompt, initialValue, validate);
        var accepted = await dialog.ShowDialog<bool>(owner);
        return accepted ? dialog.Result : null;
    });

    public Task<AttributeChange?> ShowAttributesDialogAsync(string prompt, UnixFileMode? currentMode) => OnUiThreadAsync<AttributeChange?>(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return null;
        }

        var dialog = new AttributesDialog(prompt, currentMode);
        var accepted = await dialog.ShowDialog<bool>(owner);
        return accepted ? dialog.Result : null;
    });

    public Task<(SftpSite? Site, string? Password)> ShowSftpConnectAsync() => OnUiThreadAsync<(SftpSite?, string?)>(async () =>
    {
        if (_ownerProvider() is not { } owner || _settingsService is not { } settings)
        {
            return (null, null);
        }

        var dialog = new SftpConnectDialog(settings, new KeychainSecretStore(), this);
        var accepted = await dialog.ShowDialog<bool>(owner);
        return accepted ? (dialog.Result, dialog.Password) : (null, null);
    });

    public Task ShowMessageAsync(string title, string message) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        // The confirm dialog with no cancel text: one button, and the answer is simply "I have read it".
        var dialog = new ConfirmDialog(title, message, Localization.Strings.Get("ConfirmDialog_Ok"), string.Empty, destructive: false);
        await dialog.ShowDialog<bool>(owner);
    });

    public Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false, bool defaultIsCancel = false) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return false;
        }

        var dialog = new ConfirmDialog(title, message, confirmText, cancelText, destructive, defaultIsCancel);
        return await dialog.ShowDialog<bool>(owner);
    });

    public Task<DeleteChoice> ShowDeleteConfirmAsync(IReadOnlyList<FileSystemEntry> items, bool trashAvailable, bool permanentRequested) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return DeleteChoice.Cancel;
        }

        var dialog = new DeleteConfirmDialog(items, trashAvailable, permanentRequested);
        return await dialog.ShowDialog<DeleteChoice>(owner);
    });

    public Task<ConflictResolution> ShowConflictAsync(FileConflict conflict, int remainingConflicts) => OnUiThreadAsync(async () =>
    {
        if (_ownerProvider() is not { } owner)
        {
            return new ConflictResolution(ConflictAction.Skip);
        }

        var viewModel = new ConflictDialogViewModel(
            conflict,
            remainingConflicts,
            _fileSystemService.IsDirectory(conflict.DestinationPath),
            _fileSystemService.Exists,
            SuggestUniqueName(conflict.DestinationPath));

        var dialog = new ConflictDialog { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => dialog.Close(viewModel.Result);

        var result = await dialog.ShowDialog<ConflictResolution?>(owner);
        return result ?? new ConflictResolution(ConflictAction.Cancel);
    });

    public Task ShowErrorAsync(string title, string message, string? details = null) => OnUiThreadAsync(async () =>
    {
        AppLog.Warning(details is null ? $"{title}: {message}" : $"{title}: {message}{Environment.NewLine}{details}");

        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var dialog = new ErrorDialog(title, message, details);
        await dialog.ShowDialog(owner);
    });

    public Task ShowOperationErrorsAsync(string title, IReadOnlyList<FileOperationError> errors) => OnUiThreadAsync(async () =>
    {
        foreach (var error in errors)
        {
            AppLog.Warning($"{title}: {error.Path}: {error.Message}");
        }

        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var dialog = new OperationErrorsDialog(title, errors);
        await dialog.ShowDialog(owner);
    });

    /// <summary>The active window of the desktop lifetime, else its main window, else null.</summary>
    private static Window? FindActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.FirstOrDefault(window => window.IsActive && window.IsVisible) ?? desktop.MainWindow;
    }

    private static string SuggestUniqueName(string destinationPath)
    {
        var name = Path.GetFileName(destinationPath);
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(directory))
        {
            return name;
        }

        try
        {
            return PathUtils.GetUniqueName(directory, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return name;
        }
    }

    private static Task OnUiThreadAsync(Func<Task> action)
    {
        return Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
    }

    private static Task<T> OnUiThreadAsync<T>(Func<Task<T>> action)
    {
        return Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
    }
}
