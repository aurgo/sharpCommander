using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Coordinates the dual-pane file manager: tabs (each owning its two panels), the commands behind the function
/// keys, menus and shortcuts, the clipboard, favorites, theme and the status bar. Every file operation goes through
/// <see cref="IFileOperationsService"/>, so confirmation, progress and error reporting are the same everywhere.
/// Commands never throw: failures are logged and shown in an error dialog.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileOperationsService _operations;
    private readonly ITrashService _trashService;
    private readonly ThemeService _themeService;
    private bool _disposed;

    [ObservableProperty]
    private TabViewModel? _currentTab;

    [ObservableProperty]
    private FilePanelViewModel _leftPanel;

    [ObservableProperty]
    private FilePanelViewModel _rightPanel;

    [ObservableProperty]
    private FilePanelViewModel? _activePanel;

    /// <summary>The last status line, prefixed with the time it was set.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesPanel = true;

    /// <summary>The theme in use: "System", "Light" or "Dark".</summary>
    [ObservableProperty]
    private string _currentTheme = ThemeService.SystemTheme;

    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IFileOperationsService fileOperationsService,
        ITrashService trashService,
        ThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(fileOperationsService);
        ArgumentNullException.ThrowIfNull(trashService);
        ArgumentNullException.ThrowIfNull(themeService);

        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _operations = fileOperationsService;
        _trashService = trashService;
        _themeService = themeService;

        _operations.OperationCompleted += OnOperationCompleted;

        var tab = CreateTab();
        Tabs.Add(tab);
        _leftPanel = tab.LeftPanel;
        _rightPanel = tab.RightPanel;
        _activePanel = tab.ActivePanel;
        CurrentTab = tab;
    }

    public string Title => "SharpCommander - File Manager";

    /// <summary>The application version as built (single-sourced from Directory.Build.props).</summary>
    public string Version { get; } = ReadVersion();

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>State of the running batch operation, for the status bar (progress, current file, cancel).</summary>
    public IFileOperationsService Operations => _operations;

    /// <summary>True when deleting can go to the operating system trash.</summary>
    public bool IsTrashAvailable => _trashService.IsSupported;

    /// <summary>The theme names, in menu order.</summary>
    public IReadOnlyList<string> Themes => ThemeService.Themes;

    /// <summary>Window width restored from the settings after <see cref="InitializeAsync"/>, null when unknown.</summary>
    public double? SavedWindowWidth { get; private set; }

    /// <summary>Window height restored from the settings after <see cref="InitializeAsync"/>, null when unknown.</summary>
    public double? SavedWindowHeight { get; private set; }

    /// <summary>Whether the window was maximized when the settings were saved.</summary>
    public bool SavedWindowMaximized { get; private set; }

    /// <summary>Raised by <see cref="ExitCommand"/>; the window closes itself in response.</summary>
    public event EventHandler? ExitRequested;

    // ---- lifecycle --------------------------------------------------------------------------------------

    /// <summary>Loads the settings, applies the theme and navigates the first tab to the saved folders.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _settingsService.LoadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be loaded.", ex);
            await _dialogService.ShowErrorAsync("Settings", $"The settings could not be loaded: {ex.Message}", ex.ToString());
        }

        var settings = _settingsService.Settings;
        ApplyTheme(settings.Theme);
        ShowFavoritesPanel = settings.FavoritesPanelVisible;
        SavedWindowWidth = settings.WindowWidth;
        SavedWindowHeight = settings.WindowHeight;
        SavedWindowMaximized = string.Equals(settings.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase);

        var leftPath = settings.LastLeftPanelPath ?? _fileSystemService.GetDefaultDirectory();
        var rightPath = settings.LastRightPanelPath ?? _fileSystemService.GetDefaultDirectory();

        if (CurrentTab is { } tab)
        {
            await RunGuardedAsync("Startup", () => tab.InitializeAsync(leftPath, rightPath));
        }

        SetStatus("Ready.");
    }

    /// <summary>Records the window size for <see cref="SaveStateAsync"/>; call it when the window is resized or closing.</summary>
    public void RememberWindowBounds(double width, double height, bool isMaximized)
    {
        var settings = _settingsService.Settings;
        if (!isMaximized && width > 0 && height > 0)
        {
            settings.WindowWidth = width;
            settings.WindowHeight = height;
        }

        settings.WindowState = isMaximized ? "Maximized" : "Normal";
    }

    /// <summary>Copies the panel paths, favorites panel visibility and theme into the settings without saving.</summary>
    public void CaptureState()
    {
        var settings = _settingsService.Settings;
        settings.LastLeftPanelPath = LeftPanel.CurrentPath;
        settings.LastRightPanelPath = RightPanel.CurrentPath;
        settings.FavoritesPanelVisible = ShowFavoritesPanel;
        settings.Theme = _themeService.CurrentTheme;
    }

    /// <summary>Captures the state and writes the settings now. Safe to block on: it never resumes on the UI thread.</summary>
    public Task SaveStateAsync()
    {
        CaptureState();
        return _settingsService.SaveAsync();
    }

    /// <summary>Saves the state and flushes any pending settings write; await it before closing the window.</summary>
    public async Task ShutdownAsync()
    {
        // A copy or move still writing to disk is cancelled and awaited first: the engine removes the
        // partially written destination file, so exiting never leaves a truncated file under its final name.
        try
        {
            if (_operations.IsRunning)
            {
                _operations.Cancel();
                await _operations.WhenIdleAsync().WaitAsync(ShutdownGracePeriod);
            }
        }
        catch (TimeoutException)
        {
            AppLog.Warning("A file operation did not stop within the shutdown grace period.");
        }
        catch (Exception ex)
        {
            AppLog.Error("The running file operation could not be stopped on exit.", ex);
        }

        try
        {
            await SaveStateAsync();
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be saved on exit.", ex);
        }
    }

    /// <summary>How long the window waits for a running batch to stop before it closes anyway.</summary>
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>Makes a panel of the current tab the active one.</summary>
    public void SetActivePanel(FilePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (CurrentTab is not { } tab || (!ReferenceEquals(panel, tab.LeftPanel) && !ReferenceEquals(panel, tab.RightPanel)))
        {
            return;
        }

        tab.SetActivePanel(panel);
        ActivePanel = panel;
    }

    /// <summary>
    /// Handles files dropped on <paramref name="target"/>: moves them when <paramref name="move"/>, else copies them,
    /// through the operations service like every other command.
    /// </summary>
    public Task DropAsync(FilePanelViewModel target, IReadOnlyList<string> paths, bool move)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paths);

        return RunGuardedAsync(move ? "Move" : "Copy", async () =>
        {
            if (paths.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(target.CurrentPath))
            {
                SetStatus("Files cannot be dropped on the Computer view.");
                return;
            }

            var result = move
                ? await _operations.MoveAsync(paths, target.CurrentPath)
                : await _operations.CopyAsync(paths, target.CurrentPath);

            if (result.Started)
            {
                await RefreshPanelsAsync(LeftPanel, RightPanel);
            }
        });
    }

    partial void OnCurrentTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            DetachTab(oldValue);
        }

        if (newValue is null)
        {
            return;
        }

        AttachTab(newValue);
        LeftPanel = newValue.LeftPanel;
        RightPanel = newValue.RightPanel;
        ActivePanel = newValue.ActivePanel;
    }

    // ---- file operations ------------------------------------------------------------------------------

    [RelayCommand]
    private Task CopyAsync() => TransferToOtherPanelAsync(FileOperationKind.Copy);

    [RelayCommand]
    private Task MoveAsync() => TransferToOtherPanelAsync(FileOperationKind.Move);

    [RelayCommand]
    private Task DeleteAsync() => DeleteSelectedAsync(permanentRequested: false);

    [RelayCommand]
    private Task DeletePermanentAsync() => DeleteSelectedAsync(permanentRequested: true);

    [RelayCommand]
    private Task RenameAsync() => RunGuardedAsync("Rename", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (panel.SelectedEntry is not { } entry || entry.EntryType is FileSystemEntryType.ParentDirectory or FileSystemEntryType.Drive)
        {
            SetStatus("Select a file or folder to rename.");
            return;
        }

        var newPath = await _operations.RenameAsync(entry);
        if (newPath is null)
        {
            return;
        }

        await panel.RefreshAsync();
        SelectByPath(panel, newPath);
        panel.RequestFocus();
        SetStatus($"Renamed '{entry.Name}' to '{Path.GetFileName(newPath)}'.");
    });

    [RelayCommand]
    private Task NewFolderAsync() => RunGuardedAsync("New Folder", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (string.IsNullOrEmpty(panel.CurrentPath))
        {
            SetStatus("Folders cannot be created in the Computer view.");
            return;
        }

        var created = await _operations.CreateDirectoryAsync(panel.CurrentPath);
        if (created is null)
        {
            return;
        }

        await panel.RefreshAsync();
        SelectByPath(panel, created);
        panel.RequestFocus();
        SetStatus($"Created folder '{Path.GetFileName(created)}'.");
    });

    [RelayCommand]
    private Task ViewAsync() => RunGuardedAsync("View", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to view.");
            return;
        }

        await _dialogService.ShowViewerAsync(entry.FullPath);
    });

    [RelayCommand]
    private Task EditAsync() => RunGuardedAsync("Edit", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to edit.");
            return;
        }

        if (await _operations.OpenAsync(entry))
        {
            SetStatus($"Opened '{entry.Name}'.");
        }
    });

    [RelayCommand]
    private Task RefreshAsync() => RunGuardedAsync("Refresh", () => RefreshPanelsAsync(LeftPanel, RightPanel));

    private Task TransferToOtherPanelAsync(FileOperationKind kind)
    {
        var title = kind == FileOperationKind.Copy ? "Copy" : "Move";
        return RunGuardedAsync(title, async () =>
        {
            if (ActivePanel is not { } source || CurrentTab is not { } tab)
            {
                return;
            }

            var items = source.GetSelectedItems();
            if (items.Count == 0)
            {
                SetStatus("Nothing selected.");
                return;
            }

            var target = tab.OtherPanel(source);
            if (string.IsNullOrEmpty(target.CurrentPath))
            {
                SetStatus("Open a destination folder in the other panel first.");
                return;
            }

            var anchor = source.FilteredEntries.IndexOf(items[0]);
            var paths = items.Select(item => item.FullPath).ToList();

            var result = kind == FileOperationKind.Copy
                ? await _operations.CopyAsync(paths, target.CurrentPath)
                : await _operations.MoveAsync(paths, target.CurrentPath);

            if (!result.Started)
            {
                return;
            }

            if (kind == FileOperationKind.Copy)
            {
                await RefreshPanelsAsync(target);
            }
            else
            {
                await RefreshPanelsAsync(source, target);
                SelectAt(source, anchor);
            }

            source.RequestFocus();
        });
    }

    private Task DeleteSelectedAsync(bool permanentRequested) => RunGuardedAsync("Delete", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        var anchor = panel.FilteredEntries.IndexOf(items[0]);
        var result = await _operations.DeleteAsync(items, permanentRequested);
        if (!result.Started)
        {
            return;
        }

        await RefreshPanelsAsync(panel);
        SelectAt(panel, anchor);
        panel.RequestFocus();
    });

    // ---- clipboard --------------------------------------------------------------------------------------

    [RelayCommand]
    private Task CopyToClipboardAsync() => RunGuardedAsync("Copy", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        await _clipboardService.CopyAsync(items);
        SetStatus($"Copied {Plural(items.Count, "item")} to the clipboard.");
    });

    [RelayCommand]
    private Task CutToClipboardAsync() => RunGuardedAsync("Cut", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        await _clipboardService.CutAsync(items);
        SetStatus($"Cut {Plural(items.Count, "item")} to the clipboard.");
    });

    [RelayCommand]
    private Task PasteFromClipboardAsync() => RunGuardedAsync("Paste", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (string.IsNullOrEmpty(panel.CurrentPath))
        {
            SetStatus("Files cannot be pasted in the Computer view.");
            return;
        }

        var paths = await _clipboardService.GetPathsAsync();
        if (paths.Count == 0)
        {
            SetStatus("The clipboard holds no files.");
            return;
        }

        var move = _clipboardService.IsCutMode;
        var result = move
            ? await _operations.MoveAsync(paths, panel.CurrentPath)
            : await _operations.CopyAsync(paths, panel.CurrentPath);

        if (!result.Started)
        {
            return;
        }

        if (move && !result.Cancelled)
        {
            // The cut files are gone from their source; the clipboard must not move them again.
            await _clipboardService.ClearAsync();
        }

        await RefreshPanelsAsync(LeftPanel, RightPanel);
        panel.RequestFocus();
    });

    // ---- selection, panels, tabs ----------------------------------------------------------------------

    [RelayCommand]
    private void SelectAll()
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        panel.SelectedEntries.Clear();
        foreach (var entry in panel.FilteredEntries)
        {
            if (entry.EntryType != FileSystemEntryType.ParentDirectory)
            {
                panel.SelectedEntries.Add(entry);
            }
        }

        SetStatus($"Selected {Plural(panel.SelectedEntries.Count, "item")}.");
    }

    [RelayCommand]
    private void ToggleFavoritesPanel()
    {
        ShowFavoritesPanel = !ShowFavoritesPanel;
        _settingsService.Settings.FavoritesPanelVisible = ShowFavoritesPanel;
        _settingsService.RequestSave();
    }

    [RelayCommand]
    private Task ToggleFavoriteAsync() => RunGuardedAsync("Favorites", async () =>
    {
        if (ActivePanel is { } panel)
        {
            await panel.ToggleFavoriteCommand.ExecuteAsync(null);
        }
    });

    [RelayCommand]
    private void ToggleSearch()
    {
        ActivePanel?.ToggleSearchCommand.Execute(null);
    }

    [RelayCommand]
    private Task SyncPanelsAsync() => RunGuardedAsync("Sync panels", async () =>
    {
        if (ActivePanel is not { } source || CurrentTab is not { } tab)
        {
            return;
        }

        await tab.OtherPanel(source).NavigateToCommand.ExecuteAsync(source.CurrentPath);
    });

    [RelayCommand]
    private Task SwapPanelsAsync() => RunGuardedAsync("Swap panels", async () =>
    {
        var leftPath = LeftPanel.CurrentPath;
        var rightPath = RightPanel.CurrentPath;

        await Task.WhenAll(
            LeftPanel.NavigateToCommand.ExecuteAsync(rightPath),
            RightPanel.NavigateToCommand.ExecuteAsync(leftPath));
    });

    [RelayCommand]
    private Task NewTabAsync() => RunGuardedAsync("New tab", async () =>
    {
        var leftPath = LeftPanel.CurrentPath;
        var rightPath = RightPanel.CurrentPath;

        var tab = CreateTab();
        await tab.InitializeAsync(leftPath, rightPath);

        Tabs.Add(tab);
        CurrentTab = tab;
        SetStatus("New tab opened.");
    });

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        tab ??= CurrentTab;
        if (tab is null)
        {
            return;
        }

        if (Tabs.Count <= 1)
        {
            SetStatus("The last tab cannot be closed.");
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (ReferenceEquals(CurrentTab, tab))
        {
            CurrentTab = Tabs[index > 0 ? index - 1 : 1];
        }

        Tabs.RemoveAt(index);
        tab.Dispose();
        SetStatus("Tab closed.");
    }

    [RelayCommand]
    private void CloseCurrentTab() => CloseTab(CurrentTab);

    [RelayCommand]
    private void NextTab() => CycleTab(1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    [RelayCommand]
    private void SelectTab(TabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab))
        {
            CurrentTab = tab;
        }
    }

    // ---- favorites --------------------------------------------------------------------------------------

    [RelayCommand]
    private Task RemoveFavoriteAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null)
        {
            return;
        }

        await _settingsService.RemoveFavoriteAsync(favorite.Path);
        ReloadFavorites(except: null);
        SetStatus($"Removed '{favorite.Name}' from the favorites.");
    });

    [RelayCommand]
    private Task RenameFavoriteAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null)
        {
            return;
        }

        var name = await _dialogService.ShowInputDialogAsync(
            "Rename Favorite",
            $"New name for '{favorite.Name}':",
            favorite.Name,
            value => string.IsNullOrWhiteSpace(value) ? "The name cannot be empty." : null);

        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, favorite.Name, StringComparison.Ordinal))
        {
            return;
        }

        await _settingsService.RenameFavoriteAsync(favorite.Path, name);
        ReloadFavorites(except: null);
        SetStatus($"Renamed favorite to '{name.Trim()}'.");
    });

    [RelayCommand]
    private Task OpenFavoriteInOtherPanelAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null || CurrentTab is not { } tab)
        {
            return;
        }

        var target = tab.OtherPanel(ActivePanel ?? tab.LeftPanel);
        await target.NavigateToCommand.ExecuteAsync(favorite.Path);
    });

    [RelayCommand]
    private Task RestoreDefaultFavoritesAsync() => RunGuardedAsync("Favorites", async () =>
    {
        await _settingsService.RestoreDefaultFavoritesAsync();
        ReloadFavorites(except: null);
        SetStatus("Default favorites restored.");
    });

    /// <summary>
    /// Moves a favorite to <paramref name="newIndex"/> in the list (drag reorder), renumbers the order of every
    /// favorite, schedules a settings save and reloads the lists of all panels.
    /// </summary>
    public Task MoveFavoriteAsync(FavoriteItem favorite, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        return RunGuardedAsync("Favorites", () =>
        {
            var ordered = _settingsService.Settings.Favorites.OrderBy(item => item.Order).ToList();
            var oldIndex = ordered.IndexOf(favorite);
            if (oldIndex < 0)
            {
                return Task.CompletedTask;
            }

            ordered.RemoveAt(oldIndex);
            ordered.Insert(Math.Clamp(newIndex, 0, ordered.Count), favorite);
            for (var index = 0; index < ordered.Count; index++)
            {
                ordered[index].Order = index;
            }

            _settingsService.RequestSave();
            ReloadFavorites(except: null);
            return Task.CompletedTask;
        });
    }

    // ---- tools and theme --------------------------------------------------------------------------------

    [RelayCommand]
    private Task ShowAdvancedSearchAsync() => RunGuardedAsync("Search", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var entry = await _dialogService.ShowAdvancedSearchDialogAsync(panel.CurrentPath);
        if (entry is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath;
        await panel.NavigateToCommand.ExecuteAsync(folder);
        SelectByPath(panel, entry.FullPath);
        panel.RequestFocus();
    });

    [RelayCommand]
    private Task ShowMassRenameAsync() => RunGuardedAsync("Mass Rename", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var paths = panel.GetSelectedItems().Select(item => item.FullPath).ToList();
        if (paths.Count == 0)
        {
            SetStatus("Select the files to rename first.");
            return;
        }

        if (await _dialogService.ShowMassRenameDialogAsync(paths))
        {
            await panel.RefreshAsync();
            panel.RequestFocus();
        }
    });

    [RelayCommand]
    private Task CalculateHashAsync() => RunGuardedAsync("Checksums", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to calculate its checksums.");
            return;
        }

        await _dialogService.ShowHashDialogAsync(entry.FullPath);
    });

    [RelayCommand]
    private void SetTheme(string? theme)
    {
        var name = ApplyTheme(theme);
        _settingsService.Settings.Theme = name;
        _settingsService.RequestSave();
        SetStatus($"Theme: {name}.");
    }

    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private TabViewModel CreateTab()
    {
        return new TabViewModel(_fileSystemService, _settingsService, _dialogService, _clipboardService, _operations);
    }

    private void AttachTab(TabViewModel tab)
    {
        tab.LeftPanel.FavoritesChanged += OnFavoritesChanged;
        tab.RightPanel.FavoritesChanged += OnFavoritesChanged;
        tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void DetachTab(TabViewModel tab)
    {
        tab.LeftPanel.FavoritesChanged -= OnFavoritesChanged;
        tab.RightPanel.FavoritesChanged -= OnFavoritesChanged;
        tab.PropertyChanged -= OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TabViewModel tab && ReferenceEquals(tab, CurrentTab) && e.PropertyName == nameof(TabViewModel.ActivePanel))
        {
            ActivePanel = tab.ActivePanel;
        }
    }

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        ReloadFavorites(except: sender as FilePanelViewModel);
    }

    /// <summary>Reloads the favorites list (and the star state) of every panel of every tab.</summary>
    private void ReloadFavorites(FilePanelViewModel? except)
    {
        foreach (var tab in Tabs)
        {
            foreach (var panel in new[] { tab.LeftPanel, tab.RightPanel })
            {
                if (ReferenceEquals(panel, except))
                {
                    continue;
                }

                panel.LoadFavorites();
                panel.IsFavorite = !string.IsNullOrEmpty(panel.CurrentPath) && _settingsService.IsFavorite(panel.CurrentPath);
            }
        }
    }

    private void OnOperationCompleted(object? sender, FileOperationResult result)
    {
        SetStatus(Describe(result));
    }

    private void CycleTab(int step)
    {
        if (Tabs.Count < 2 || CurrentTab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(CurrentTab);
        CurrentTab = Tabs[((index + step) % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    private string ApplyTheme(string? theme)
    {
        _themeService.Apply(theme);
        CurrentTheme = _themeService.CurrentTheme;
        return CurrentTheme;
    }

    private static Task RefreshPanelsAsync(params FilePanelViewModel[] panels)
    {
        return Task.WhenAll(panels.Distinct().Select(panel => panel.RefreshAsync()));
    }

    /// <summary>Selects the entry at <paramref name="index"/>, or the last one when the list got shorter.</summary>
    private static void SelectAt(FilePanelViewModel panel, int index)
    {
        if (index < 0 || panel.FilteredEntries.Count == 0)
        {
            return;
        }

        Select(panel, panel.FilteredEntries[Math.Min(index, panel.FilteredEntries.Count - 1)]);
    }

    /// <summary>Selects and reveals the entry with that path (a renamed, created or found entry may be off-screen).</summary>
    private static void SelectByPath(FilePanelViewModel panel, string path)
    {
        panel.SelectPath(path);
    }

    private static void Select(FilePanelViewModel panel, FileSystemEntry entry)
    {
        panel.SelectedEntry = entry;
        panel.SelectedEntries.Clear();
        panel.SelectedEntries.Add(entry);
    }

    private async Task RunGuardedAsync(string title, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{title} cancelled.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"{title} failed.", ex);
            SetStatus($"{title} failed: {ex.Message}");
            await _dialogService.ShowErrorAsync(title, ex.Message, ex.ToString());
        }
    }

    private void SetStatus(string message)
    {
        StatusMessage = $"{DateTime.Now:HH:mm:ss}  {message}";
    }

    private static string Describe(FileOperationResult result)
    {
        var verb = result.Kind switch
        {
            FileOperationKind.Copy => "Copied",
            FileOperationKind.Move => "Moved",
            FileOperationKind.Delete => "Deleted",
            FileOperationKind.Trash => "Moved to the trash",
            _ => "Processed"
        };

        var details = new List<string>();
        if (result.Skipped > 0)
        {
            details.Add($"{result.Skipped} skipped");
        }

        if (result.Failed > 0)
        {
            details.Add($"{result.Failed} failed");
        }

        var text = $"{verb} {Plural(result.Succeeded, "item")}";
        if (details.Count > 0)
        {
            text += $" ({string.Join(", ", details)})";
        }

        return result.Cancelled ? $"Cancelled. {text}." : $"{text}.";
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }

    private static string ReadVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadata = informational.IndexOf('+');
            return metadata > 0 ? informational[..metadata] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operations.OperationCompleted -= OnOperationCompleted;

        if (CurrentTab is { } current)
        {
            DetachTab(current);
        }

        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }

        Tabs.Clear();

        // Best effort; ShutdownAsync is the awaited path.
        _ = FlushSettingsQuietlyAsync();
    }

    private async Task FlushSettingsQuietlyAsync()
    {
        try
        {
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be flushed.", ex);
        }
    }
}
