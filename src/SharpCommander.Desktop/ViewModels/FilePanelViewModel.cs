using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// One file panel: the folder it shows, its sorted and filtered entries, the selection, the navigation history
/// and the favorites star. Navigation (validate, list, reset the selection, watch, record history) is separate
/// from refresh (re-list and patch the collections in place, keeping selection and scroll position); both go
/// through one guarded loader so a newer request cancels an older one and stale results are dropped. Every
/// file operation is delegated to <see cref="IFileOperationsService"/>; commands never throw.
/// </summary>
public sealed partial class FilePanelViewModel : ObservableObject, IDisposable
{
    /// <summary>Sort column value: by name.</summary>
    public const string SortByName = "Name";

    /// <summary>Sort column value: by size.</summary>
    public const string SortBySize = "Size";

    /// <summary>Sort column value: by modification date.</summary>
    public const string SortByModified = "Modified";

    private const string ComputerDisplayName = "Computer";
    private const string AscendingIndicator = "\u25B2";
    private const string DescendingIndicator = "\u25BC";
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromMilliseconds(200);

    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileOperationsService _operations;
    private readonly FileSystemWatcherService _watcher = new();
    private readonly SyncedObservableCollection<FileSystemEntry> _entries = new();
    private readonly SyncedObservableCollection<FileSystemEntry> _filteredEntries = new();
    private readonly SyncedObservableCollection<FileSystemEntry> _selectedEntries = new();
    private readonly SyncedObservableCollection<NavigationHistoryItem> _history = new();
    private readonly SyncedObservableCollection<FavoriteItem> _favorites = new();
    private DispatcherTimer? _refreshTimer;
    private LoadOperation? _currentLoad;
    private bool _refreshPending;
    private bool _statusUpdatePending;
    private bool _applyingSettings;
    private string _sortColumn = SortByName;
    private bool _sortDescending;
    private bool _disposed;

    /// <summary>The folder shown, empty for the Computer view.</summary>
    [ObservableProperty]
    private string _currentPath = string.Empty;

    /// <summary>The folder shown as text, "Computer" for the drives view.</summary>
    [ObservableProperty]
    private string _displayPath = ComputerDisplayName;

    /// <summary>The text of the path box; typing a path and pressing Enter navigates.</summary>
    [ObservableProperty]
    private string _editablePath = string.Empty;

    /// <summary>The entry that has the cursor; part of <see cref="SelectedEntries"/> when anything is selected.</summary>
    [ObservableProperty]
    private FileSystemEntry? _selectedEntry;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True while the drives are listed instead of a folder.</summary>
    [ObservableProperty]
    private bool _isRootView;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive;

    /// <summary>True when the current folder is a favorite. Set by the main view model when favorites change elsewhere.</summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Whether hidden entries (Hidden attribute, dotfiles on Unix) are listed.</summary>
    [ObservableProperty]
    private bool _showHiddenFiles;

    public FilePanelViewModel(
        IFileSystemService fileSystemService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IFileOperationsService fileOperationsService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(fileOperationsService);

        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _operations = fileOperationsService;

        _watcher.Changed += OnWatcherChanged;
        _watcher.Restarted += OnWatcherRestarted;
        _selectedEntries.CollectionChanged += OnSelectedEntriesChanged;
    }

    // ---- collections and derived state ------------------------------------------------------------------

    /// <summary>All entries of the folder, sorted; updated in place on refresh.</summary>
    public ObservableCollection<FileSystemEntry> Entries => _entries;

    /// <summary>The entries shown: <see cref="Entries"/> minus hidden ones and the filter; updated in place.</summary>
    public ObservableCollection<FileSystemEntry> FilteredEntries => _filteredEntries;

    /// <summary>The multi-selection. The view mirrors it into the list and back.</summary>
    public ObservableCollection<FileSystemEntry> SelectedEntries => _selectedEntries;

    /// <summary>Recently visited folders, most recent first.</summary>
    public ObservableCollection<NavigationHistoryItem> NavigationHistory => _history;

    /// <summary>The favorites, in user order.</summary>
    public ObservableCollection<FavoriteItem> Favorites => _favorites;

    /// <summary>The sort column: "Name", "Size" or "Modified".</summary>
    public string SortColumn
    {
        get => _sortColumn;
        private set
        {
            if (SetProperty(ref _sortColumn, value))
            {
                NotifySortIndicators();
            }
        }
    }

    /// <summary>True when the sort column is descending.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        private set
        {
            if (SetProperty(ref _sortDescending, value))
            {
                NotifySortIndicators();
            }
        }
    }

    public bool IsSortedByName => SortColumn == SortByName;

    public bool IsSortedBySize => SortColumn == SortBySize;

    public bool IsSortedByModified => SortColumn == SortByModified;

    /// <summary>Arrow shown in the Name header, empty when another column sorts.</summary>
    public string NameSortIndicator => IndicatorFor(SortByName);

    public string SizeSortIndicator => IndicatorFor(SortBySize);

    public string ModifiedSortIndicator => IndicatorFor(SortByModified);

    // ---- events -----------------------------------------------------------------------------------------

    /// <summary>Raised when this panel added or removed a favorite, so other panels reload their lists.</summary>
    public event EventHandler? FavoritesChanged;

    /// <summary>Raised when the panel wants the keyboard focus back on its list.</summary>
    public event EventHandler? FocusRequested;

    /// <summary>Raised when the selection changed programmatically and the entry should be scrolled into view.</summary>
    public event EventHandler<FileSystemEntry>? SelectionRevealRequested;

    // ---- lifecycle --------------------------------------------------------------------------------------

    /// <summary>
    /// Loads favorites, history and view settings, then navigates: to <paramref name="path"/>, to the default
    /// directory when it is null or no longer exists, or to the Computer view when it is empty.
    /// </summary>
    public async Task InitializeAsync(string? path = null)
    {
        LoadFavorites();
        LoadHistory();
        LoadViewSettings();

        var target = path ?? _fileSystemService.GetDefaultDirectory();
        if (!string.IsNullOrEmpty(target) && !_fileSystemService.IsDirectory(target))
        {
            target = _fileSystemService.GetDefaultDirectory();
        }

        if (!string.IsNullOrEmpty(target) && !_fileSystemService.IsDirectory(target))
        {
            target = string.Empty;
        }

        await NavigateToAsync(target);
    }

    /// <summary>Reloads the favorites list from the settings.</summary>
    public void LoadFavorites()
    {
        _favorites.ReplaceAll(_settingsService.Settings.Favorites.OrderBy(favorite => favorite.Order).ToList());
    }

    /// <summary>Asks the view to put the keyboard focus on the selected row.</summary>
    public void RequestFocus()
    {
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentLoad?.Cancel();
        _refreshTimer?.Stop();
        _watcher.Changed -= OnWatcherChanged;
        _watcher.Restarted -= OnWatcherRestarted;
        _watcher.Dispose();
    }

    // ---- selection helpers used by the window and the view ----------------------------------------------

    /// <summary>The selected entries without the ".." row; the cursor entry when nothing is multi-selected.</summary>
    public IReadOnlyList<FileSystemEntry> GetSelectedItems()
    {
        IEnumerable<FileSystemEntry> items = _selectedEntries.Count > 0
            ? _selectedEntries
            : SelectedEntry is { } current ? [current] : [];

        return items.Where(item => item.EntryType != FileSystemEntryType.ParentDirectory).ToList();
    }

    /// <summary>Selects the entry with the given full path, reveals it and returns true when it is listed.</summary>
    public bool SelectPath(string path)
    {
        if (FindByPath(path) is not { } entry)
        {
            return false;
        }

        SetSelection(entry, reveal: true);
        return true;
    }

    /// <summary>
    /// Type-ahead: selects the next entry (after the cursor, wrapping around) whose name starts with
    /// <paramref name="prefix"/>. A multi-character prefix keeps the cursor entry when it already matches, so
    /// typing a name letter by letter stays on the first match while repeating a letter cycles through them.
    /// </summary>
    public bool IncrementalSearch(string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || _filteredEntries.Count == 0)
        {
            return false;
        }

        var count = _filteredEntries.Count;
        var start = SelectedEntry is { } current ? _filteredEntries.IndexOf(current) : -1;

        if (prefix.Length > 1 && start >= 0 && Matches(_filteredEntries[start]))
        {
            SetSelection(_filteredEntries[start], reveal: true);
            return true;
        }

        for (var step = 1; step <= count; step++)
        {
            var index = (start + step) % count;
            if (Matches(_filteredEntries[index]))
            {
                SetSelection(_filteredEntries[index], reveal: true);
                return true;
            }
        }

        return false;

        bool Matches(FileSystemEntry entry) => entry.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Handles files dropped on this panel: moves them when <paramref name="move"/>, else copies them, through
    /// the operations service (which refuses a folder dropped on itself), then refreshes the listing.
    /// </summary>
    public Task DropAsync(IReadOnlyList<string> paths, bool move)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return RunGuardedAsync(move ? "Move" : "Copy", async () =>
        {
            if (paths.Count == 0)
            {
                return;
            }

            if (IsRootView || string.IsNullOrEmpty(CurrentPath))
            {
                StatusText = "Files cannot be dropped on the Computer view.";
                return;
            }

            var result = move
                ? await _operations.MoveAsync(paths, CurrentPath)
                : await _operations.CopyAsync(paths, CurrentPath);

            if (result.Started)
            {
                await RefreshAsync();
            }
        });
    }

    // ---- navigation -------------------------------------------------------------------------------------

    /// <summary>
    /// Navigates to a folder (null or empty: the Computer view). A missing path is reported and the current
    /// folder is kept; a file path opens the file (with the executable confirmation) without touching the panel.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task NavigateToAsync(string? path)
    {
        if (_disposed)
        {
            return;
        }

        var requested = path?.Trim();
        if (string.IsNullOrEmpty(requested))
        {
            await NavigateToDirectoryAsync(null);
            return;
        }

        string fullPath;
        try
        {
            fullPath = PathUtils.NormalizeFullPath(requested);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await ReportPathNotFoundAsync(requested);
            return;
        }

        if (_fileSystemService.IsDirectory(fullPath))
        {
            await NavigateToDirectoryAsync(fullPath);
        }
        else if (_fileSystemService.Exists(fullPath))
        {
            EditablePath = CurrentPath;
            await RunGuardedAsync("Open", () => OpenFileAsync(fullPath));
        }
        else
        {
            await ReportPathNotFoundAsync(requested);
        }
    }

    /// <summary>Re-lists the current folder and patches the entries in place; history, watcher and settings are untouched.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAsync()
    {
        if (_disposed || (!IsRootView && string.IsNullOrEmpty(CurrentPath)))
        {
            return;
        }

        if (!IsRootView && !_fileSystemService.IsDirectory(CurrentPath))
        {
            await NavigateToNearestExistingAsync(CurrentPath);
            return;
        }

        var operation = BeginLoad(isNavigation: false);
        if (operation is null)
        {
            return;
        }

        var directory = IsRootView ? null : CurrentPath;
        try
        {
            var entries = await _fileSystemService.GetEntriesAsync(directory, operation.Token);
            if (IsCurrent(operation))
            {
                ApplyRefresh(entries);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation or refresh.
        }
        catch (UnauthorizedAccessException)
        {
            if (IsCurrent(operation))
            {
                StatusText = $"Access denied: {directory}";
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Refreshing '{directory ?? ComputerDisplayName}' failed.", ex);
            if (IsCurrent(operation))
            {
                StatusText = $"Refresh failed: {ex.Message}";
            }
        }
        finally
        {
            EndLoad(operation);
        }
    }

    [RelayCommand]
    private Task NavigateToPathAsync()
    {
        return NavigateToAsync(EditablePath);
    }

    [RelayCommand]
    private Task NavigateToHistoryItemAsync(NavigationHistoryItem? item)
    {
        return item is null ? Task.CompletedTask : NavigateToAsync(item.Path);
    }

    [RelayCommand]
    private Task NavigateToFavoriteAsync(FavoriteItem? favorite)
    {
        return favorite is null ? Task.CompletedTask : NavigateToAsync(favorite.Path);
    }

    /// <summary>Goes to the parent folder; a drive root goes to the Computer view.</summary>
    [RelayCommand]
    public Task NavigateUpAsync()
    {
        if (IsRootView || string.IsNullOrEmpty(CurrentPath))
        {
            return Task.CompletedTask;
        }

        return NavigateToAsync(_fileSystemService.GetParentPath(CurrentPath) ?? string.Empty);
    }

    [RelayCommand]
    private Task NavigateToRootAsync()
    {
        return NavigateToAsync(string.Empty);
    }

    private async Task NavigateToDirectoryAsync(string? directory)
    {
        var operation = BeginLoad(isNavigation: true);
        if (operation is null)
        {
            return;
        }

        Exception? failure = null;
        var superseded = false;
        try
        {
            var entries = await _fileSystemService.GetEntriesAsync(directory, operation.Token);
            superseded = !IsCurrent(operation);
            if (!superseded)
            {
                ApplyNavigation(directory, entries);
            }
        }
        catch (OperationCanceledException)
        {
            superseded = true;
        }
        catch (Exception ex)
        {
            superseded = !IsCurrent(operation);
            failure = ex;
        }
        finally
        {
            EndLoad(operation);
        }

        if (superseded)
        {
            return;
        }

        if (failure is not null)
        {
            await ReportNavigationFailureAsync(directory, failure);
            return;
        }

        if (directory is not null)
        {
            await RecordHistoryAsync(directory);
        }
    }

    private void ApplyNavigation(string? directory, IReadOnlyList<FileSystemEntry> entries)
    {
        var previousPath = CurrentPath;
        var previousWasRoot = IsRootView;

        _entries.ReplaceAll(Sort(entries));
        CurrentPath = directory ?? string.Empty;
        IsRootView = directory is null;
        DisplayPath = directory ?? ComputerDisplayName;
        EditablePath = directory ?? string.Empty;
        IsFavorite = directory is not null && _settingsService.IsFavorite(directory);
        _filteredEntries.ReplaceAll(VisibleEntries());

        if (directory is null)
        {
            _watcher.Stop();
        }
        else
        {
            StartWatcher(directory);
        }

        SelectAfterNavigation(previousWasRoot ? null : previousPath);
        UpdateStatus();
    }

    private void ApplyRefresh(IReadOnlyList<FileSystemEntry> entries)
    {
        _entries.SyncTo(Sort(entries), PathKey, PathUtils.PathComparer);
        _filteredEntries.SyncTo(VisibleEntries(), PathKey, PathUtils.PathComparer);
        RestoreSelection();
        UpdateStatus();
    }

    /// <summary>After navigating up the folder we came from gets the cursor; otherwise the first row does.</summary>
    private void SelectAfterNavigation(string? previousPath)
    {
        FileSystemEntry? target = null;
        if (!string.IsNullOrEmpty(previousPath))
        {
            target = FindByPath(previousPath);
        }

        target ??= _filteredEntries.FirstOrDefault();
        SetSelection(target, reveal: true);
    }

    private async Task NavigateToNearestExistingAsync(string missingPath)
    {
        var candidate = _fileSystemService.GetParentPath(missingPath);
        while (!string.IsNullOrEmpty(candidate) && !_fileSystemService.IsDirectory(candidate))
        {
            candidate = _fileSystemService.GetParentPath(candidate);
        }

        StatusText = $"The folder '{missingPath}' no longer exists.";
        await NavigateToDirectoryAsync(string.IsNullOrEmpty(candidate) ? null : candidate);
    }

    private async Task RecordHistoryAsync(string directory)
    {
        try
        {
            await _settingsService.AddToHistoryAsync(directory);
            LoadHistory();
        }
        catch (Exception ex)
        {
            AppLog.Warning("The navigation history could not be updated.", ex);
        }
    }

    private async Task ReportPathNotFoundAsync(string requested)
    {
        EditablePath = CurrentPath;
        StatusText = $"Path not found: {requested}";
        await _dialogService.ShowErrorAsync("Path not found", $"The path '{requested}' does not exist.");
    }

    private async Task ReportNavigationFailureAsync(string? directory, Exception failure)
    {
        EditablePath = CurrentPath;
        var name = directory ?? ComputerDisplayName;

        if (failure is UnauthorizedAccessException)
        {
            StatusText = $"Access denied: {name}";
            await _dialogService.ShowErrorAsync("Access denied", $"The folder '{name}' cannot be read.\n{failure.Message}");
            return;
        }

        AppLog.Warning($"Navigating to '{name}' failed.", failure);
        StatusText = $"Cannot open '{name}': {failure.Message}";
        await _dialogService.ShowErrorAsync("Navigate", $"The folder '{name}' cannot be opened.\n{failure.Message}");
    }

    private async Task OpenFileAsync(string fullPath)
    {
        var info = new FileInfo(fullPath);
        var entry = new FileSystemEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            EntryType = FileSystemEntryType.File,
            Size = info.Exists ? info.Length : 0,
            LastModified = info.Exists ? info.LastWriteTime : default,
            Extension = info.Extension
        };

        if (await _operations.OpenAsync(entry))
        {
            StatusText = $"Opened '{entry.Name}'.";
        }
    }

    // ---- guarded loader (M6) ----------------------------------------------------------------------------

    /// <summary>
    /// Starts a load. A navigation cancels whatever is running; a refresh cancels a previous refresh but never a
    /// navigation (it runs once the navigation finished instead). Returns null when nothing should start.
    /// </summary>
    private LoadOperation? BeginLoad(bool isNavigation)
    {
        if (_disposed)
        {
            return null;
        }

        if (_currentLoad is { } previous)
        {
            if (!isNavigation && previous.IsNavigation)
            {
                _refreshPending = true;
                return null;
            }

            previous.Cancel();
        }

        var operation = new LoadOperation(isNavigation);
        _currentLoad = operation;
        IsLoading = true;
        return operation;
    }

    private bool IsCurrent(LoadOperation operation)
    {
        return ReferenceEquals(_currentLoad, operation) && !operation.Token.IsCancellationRequested;
    }

    private void EndLoad(LoadOperation operation)
    {
        if (ReferenceEquals(_currentLoad, operation))
        {
            _currentLoad = null;
            IsLoading = false;
        }

        operation.Dispose();

        if (_currentLoad is null && _refreshPending && !_disposed)
        {
            _refreshPending = false;
            _ = RefreshQuietlyAsync();
        }
    }

    // ---- file operations --------------------------------------------------------------------------------

    [RelayCommand]
    private Task OpenSelectedAsync() => RunGuardedAsync("Open", async () =>
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        switch (entry.EntryType)
        {
            case FileSystemEntryType.Directory:
            case FileSystemEntryType.Drive:
                await NavigateToAsync(entry.FullPath);
                break;
            case FileSystemEntryType.ParentDirectory:
                await NavigateUpAsync();
                break;
            default:
                if (await _operations.OpenAsync(entry))
                {
                    StatusText = $"Opened '{entry.Name}'.";
                }

                break;
        }
    });

    /// <summary>Deletes the selection after confirmation, to the trash when available.</summary>
    [RelayCommand]
    private Task DeleteSelectedAsync() => DeleteAsync(permanentRequested: false);

    /// <summary>Deletes the selection permanently after confirmation.</summary>
    [RelayCommand]
    private Task DeleteSelectedPermanentlyAsync() => DeleteAsync(permanentRequested: true);

    private Task DeleteAsync(bool permanentRequested) => RunGuardedAsync("Delete", async () =>
    {
        var items = GetSelectedItems();
        if (items.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        var anchor = _filteredEntries.IndexOf(items[0]);
        var result = await _operations.DeleteAsync(items, permanentRequested);
        if (!result.Started)
        {
            return;
        }

        await RefreshAsync();
        SelectAt(anchor);
        RequestFocus();
    });

    [RelayCommand]
    private Task RenameSelectedAsync() => RunGuardedAsync("Rename", async () =>
    {
        if (SelectedEntry is not { } entry || entry.EntryType is FileSystemEntryType.ParentDirectory or FileSystemEntryType.Drive)
        {
            StatusText = "Select a file or folder to rename.";
            return;
        }

        var newPath = await _operations.RenameAsync(entry);
        if (newPath is null)
        {
            return;
        }

        await RefreshAsync();
        SelectPath(newPath);
        RequestFocus();
        StatusText = $"Renamed '{entry.Name}' to '{Path.GetFileName(newPath)}'.";
    });

    [RelayCommand]
    private Task CutAsync() => RunGuardedAsync("Cut", async () =>
    {
        var items = GetSelectedItems();
        if (items.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        await _clipboardService.CutAsync(items);
        StatusText = $"Cut {Plural(items.Count, "item")} to the clipboard.";
    });

    [RelayCommand]
    private Task CopyAsync() => RunGuardedAsync("Copy", async () =>
    {
        var items = GetSelectedItems();
        if (items.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        await _clipboardService.CopyAsync(items);
        StatusText = $"Copied {Plural(items.Count, "item")} to the clipboard.";
    });

    [RelayCommand]
    private Task PasteAsync() => RunGuardedAsync("Paste", async () =>
    {
        if (IsRootView || string.IsNullOrEmpty(CurrentPath))
        {
            StatusText = "Files cannot be pasted in the Computer view.";
            return;
        }

        var paths = await _clipboardService.GetPathsAsync();
        if (paths.Count == 0)
        {
            StatusText = "The clipboard holds no files.";
            return;
        }

        var move = _clipboardService.IsCutMode;
        var result = move
            ? await _operations.MoveAsync(paths, CurrentPath)
            : await _operations.CopyAsync(paths, CurrentPath);

        if (!result.Started)
        {
            return;
        }

        if (move && !result.Cancelled)
        {
            await _clipboardService.ClearAsync();
        }

        await RefreshAsync();
        RequestFocus();
    });

    [RelayCommand]
    private Task ShowPropertiesAsync() => RunGuardedAsync("Properties", async () =>
    {
        if (SelectedEntry is not { } entry || entry.EntryType == FileSystemEntryType.ParentDirectory)
        {
            StatusText = "Select an entry to show its properties.";
            return;
        }

        await _dialogService.ShowPropertiesDialogAsync(entry);
    });

    /// <summary>Opens the folder this panel is showing in the system file manager.</summary>
    [RelayCommand]
    private Task OpenInFileExplorerAsync() => RunGuardedAsync("Open in file manager", async () =>
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            StatusText = "Open a folder first.";
            return;
        }

        await _fileSystemService.OpenInFileExplorerAsync(CurrentPath);
    });

    /// <summary>Shows the selected entry selected inside its folder in the system file manager.</summary>
    [RelayCommand]
    private Task RevealInFileExplorerAsync() => RunGuardedAsync("Show in file manager", async () =>
    {
        if (SelectedEntry is not { EntryType: FileSystemEntryType.File or FileSystemEntryType.Directory } entry)
        {
            if (string.IsNullOrEmpty(CurrentPath))
            {
                StatusText = "Select an item first.";
                return;
            }

            await _fileSystemService.OpenInFileExplorerAsync(CurrentPath);
            return;
        }

        await _fileSystemService.RevealInFileExplorerAsync(entry.FullPath);
    });

    // ---- favorites --------------------------------------------------------------------------------------

    /// <summary>Adds the current folder to the favorites, or removes it when it already is one.</summary>
    [RelayCommand]
    public Task ToggleFavoriteAsync() => RunGuardedAsync("Favorites", async () =>
    {
        if (IsRootView || string.IsNullOrEmpty(CurrentPath))
        {
            StatusText = "The Computer view cannot be a favorite.";
            return;
        }

        if (_settingsService.IsFavorite(CurrentPath))
        {
            await _settingsService.RemoveFavoriteAsync(CurrentPath);
            StatusText = "Removed from favorites.";
        }
        else
        {
            await _settingsService.AddFavoriteAsync(CurrentPath);
            StatusText = "Added to favorites.";
        }

        IsFavorite = _settingsService.IsFavorite(CurrentPath);
        LoadFavorites();
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Adds the selected folder (or the current one) to the favorites.</summary>
    [RelayCommand]
    private Task AddToFavoritesAsync() => RunGuardedAsync("Favorites", async () =>
    {
        string path;
        string? name = null;
        if (SelectedEntry is { EntryType: FileSystemEntryType.Directory } folder)
        {
            path = folder.FullPath;
            name = folder.Name;
        }
        else if (!IsRootView && !string.IsNullOrEmpty(CurrentPath))
        {
            path = CurrentPath;
        }
        else
        {
            StatusText = "Select a folder to add to the favorites.";
            return;
        }

        if (_settingsService.IsFavorite(path))
        {
            StatusText = "Already in favorites.";
            return;
        }

        await _settingsService.AddFavoriteAsync(path, name);
        IsFavorite = !string.IsNullOrEmpty(CurrentPath) && _settingsService.IsFavorite(CurrentPath);
        LoadFavorites();
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
        StatusText = "Added to favorites.";
    });

    // ---- filter, hidden files and sorting ---------------------------------------------------------------

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchActive = !IsSearchActive;
        if (!IsSearchActive)
        {
            SearchFilter = string.Empty;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchFilter = string.Empty;
        IsSearchActive = false;
    }

    [RelayCommand]
    private void ToggleShowHiddenFiles()
    {
        ShowHiddenFiles = !ShowHiddenFiles;
    }

    /// <summary>Sorts by the column ("Name", "Size", "Modified"); choosing the current column flips the direction.</summary>
    [RelayCommand]
    private void SortBy(string? column)
    {
        if (NormalizeSortColumn(column) is not { } normalized)
        {
            return;
        }

        if (normalized == SortColumn)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = normalized;
            SortDescending = false;
        }

        PersistSort();
        Resort();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDescending = !SortDescending;
        PersistSort();
        Resort();
    }

    partial void OnSearchFilterChanged(string value)
    {
        _filteredEntries.SyncTo(VisibleEntries(), PathKey, PathUtils.PathComparer);
        RestoreSelection();
        UpdateStatus();
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        if (!_applyingSettings)
        {
            _settingsService.Settings.ShowHiddenFiles = value;
            _settingsService.RequestSave();
        }

        _filteredEntries.SyncTo(VisibleEntries(), PathKey, PathUtils.PathComparer);
        RestoreSelection();
        UpdateStatus();
    }

    private void LoadViewSettings()
    {
        var settings = _settingsService.Settings;
        _applyingSettings = true;
        try
        {
            SortColumn = NormalizeSortColumn(settings.SortColumn) ?? SortByName;
            SortDescending = string.Equals(settings.SortDirection, "Descending", StringComparison.OrdinalIgnoreCase);
            ShowHiddenFiles = settings.ShowHiddenFiles;
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void PersistSort()
    {
        var settings = _settingsService.Settings;
        settings.SortColumn = SortColumn;
        settings.SortDirection = SortDescending ? "Descending" : "Ascending";
        _settingsService.RequestSave();
    }

    private void Resort()
    {
        _entries.ReplaceAll(Sort(_entries.ToList()));
        _filteredEntries.ReplaceAll(VisibleEntries());
        RestoreSelection();
        if (SelectedEntry is { } entry)
        {
            SelectionRevealRequested?.Invoke(this, entry);
        }
    }

    private List<FileSystemEntry> Sort(IReadOnlyList<FileSystemEntry> entries)
    {
        var sorted = entries.ToList();
        sorted.Sort(CompareEntries);
        return sorted;
    }

    /// <summary>".." first, then folders and drives, then files; within a group by the sort column, ties by name.</summary>
    private int CompareEntries(FileSystemEntry a, FileSystemEntry b)
    {
        var byGroup = GroupOf(a).CompareTo(GroupOf(b));
        if (byGroup != 0)
        {
            return byGroup;
        }

        var result = SortColumn switch
        {
            SortBySize => a.Size.CompareTo(b.Size),
            SortByModified => a.LastModified.CompareTo(b.LastModified),
            _ => 0
        };

        if (result == 0)
        {
            result = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        }

        return SortDescending ? -result : result;
    }

    private static int GroupOf(FileSystemEntry entry) => entry.EntryType switch
    {
        FileSystemEntryType.ParentDirectory => 0,
        FileSystemEntryType.Directory or FileSystemEntryType.Drive => 1,
        _ => 2
    };

    private static string? NormalizeSortColumn(string? column)
    {
        if (string.Equals(column, SortByName, StringComparison.OrdinalIgnoreCase))
        {
            return SortByName;
        }

        if (string.Equals(column, SortBySize, StringComparison.OrdinalIgnoreCase))
        {
            return SortBySize;
        }

        if (string.Equals(column, SortByModified, StringComparison.OrdinalIgnoreCase))
        {
            return SortByModified;
        }

        return null;
    }

    private string IndicatorFor(string column)
    {
        if (SortColumn != column)
        {
            return string.Empty;
        }

        return SortDescending ? DescendingIndicator : AscendingIndicator;
    }

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(IsSortedByName));
        OnPropertyChanged(nameof(IsSortedBySize));
        OnPropertyChanged(nameof(IsSortedByModified));
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(SizeSortIndicator));
        OnPropertyChanged(nameof(ModifiedSortIndicator));
    }

    private bool IsVisible(FileSystemEntry entry)
    {
        if (entry.EntryType == FileSystemEntryType.ParentDirectory)
        {
            return true;
        }

        if (!ShowHiddenFiles && entry.IsHidden)
        {
            return false;
        }

        var filter = SearchFilter.Trim();
        return filter.Length == 0 || entry.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private List<FileSystemEntry> VisibleEntries()
    {
        return _entries.Where(IsVisible).ToList();
    }

    // ---- selection and status ---------------------------------------------------------------------------

    private FileSystemEntry? FindByPath(string path)
    {
        return _filteredEntries.FirstOrDefault(entry => string.Equals(entry.FullPath, path, PathUtils.PathComparison));
    }

    private void SetSelection(FileSystemEntry? entry, bool reveal)
    {
        SelectedEntry = entry;
        _selectedEntries.ReplaceAll(entry is null ? [] : [entry]);

        if (reveal && entry is not null)
        {
            SelectionRevealRequested?.Invoke(this, entry);
        }
    }

    /// <summary>Selects the row at <paramref name="index"/>, or the last row when the list got shorter.</summary>
    private void SelectAt(int index)
    {
        if (index < 0 || _filteredEntries.Count == 0)
        {
            return;
        }

        SetSelection(_filteredEntries[Math.Min(index, _filteredEntries.Count - 1)], reveal: true);
    }

    /// <summary>
    /// Re-maps the selection by path after the entries changed, so replaced records stay selected and rows that
    /// disappeared drop out. The multi-selection is always rebuilt so the view re-applies it to the list.
    /// </summary>
    private void RestoreSelection()
    {
        var current = SelectedEntry is { } cursor ? FindByPath(cursor.FullPath) : null;
        var restored = new List<FileSystemEntry>(_selectedEntries.Count);
        foreach (var selected in _selectedEntries)
        {
            if (FindByPath(selected.FullPath) is { } entry)
            {
                restored.Add(entry);
            }
        }

        SelectedEntry = current;
        _selectedEntries.ReplaceAll(restored);
    }

    /// <summary>
    /// A rebuilt selection (Reset) updates the status at once; incremental adds and removes, which arrive one per
    /// row during Select All or a shift-selection, are coalesced into one update per dispatcher turn.
    /// </summary>
    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UpdateStatus();
            return;
        }

        if (_statusUpdatePending)
        {
            return;
        }

        _statusUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _statusUpdatePending = false;
            if (!_disposed)
            {
                UpdateStatus();
            }
        });
    }

    private void UpdateStatus()
    {
        if (IsRootView)
        {
            StatusText = Plural(_filteredEntries.Count, "drive");
            return;
        }

        var folders = 0;
        var files = 0;
        long size = 0;
        foreach (var entry in _filteredEntries)
        {
            switch (entry.EntryType)
            {
                case FileSystemEntryType.Directory:
                    folders++;
                    break;
                case FileSystemEntryType.File:
                    files++;
                    size += entry.Size;
                    break;
            }
        }

        var text = $"{Plural(folders, "folder")}, {Plural(files, "file")} ({FileSizeFormatter.Format(size)})";

        var selected = GetSelectedItems();
        if (selected.Count > 0)
        {
            var selectedSize = selected.Where(item => item.EntryType == FileSystemEntryType.File).Sum(item => item.Size);
            text += $", {selected.Count} selected ({FileSizeFormatter.Format(selectedSize)})";
        }

        StatusText = text;
    }

    // ---- watcher ----------------------------------------------------------------------------------------

    private void StartWatcher(string directory)
    {
        try
        {
            _watcher.Start(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The folder '{directory}' cannot be watched.", ex);
        }
    }

    private void OnWatcherChanged(object? sender, FileSystemChangedEventArgs e)
    {
        ScheduleRefresh();
    }

    private void OnWatcherRestarted(object? sender, EventArgs e)
    {
        ScheduleRefresh();
    }

    /// <summary>Coalesces watcher events into one refresh shortly after the last one, on the UI thread.</summary>
    private void ScheduleRefresh()
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _refreshTimer ??= CreateRefreshTimer();
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private DispatcherTimer CreateRefreshTimer()
    {
        var timer = new DispatcherTimer { Interval = WatcherDebounce };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RefreshQuietlyAsync();
        };

        return timer;
    }

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The panel could not be refreshed.", ex);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private void LoadHistory()
    {
        _history.ReplaceAll(_settingsService.GetRecentHistory(30));
    }

    private async Task RunGuardedAsync(string title, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{title} cancelled.";
        }
        catch (Exception ex)
        {
            AppLog.Error($"{title} failed.", ex);
            StatusText = $"{title} failed: {ex.Message}";
            await _dialogService.ShowErrorAsync(title, ex.Message, ex.ToString());
        }
    }

    private static string PathKey(FileSystemEntry entry) => entry.FullPath;

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>One list operation: its cancellation and whether it is a navigation or a refresh.</summary>
    private sealed class LoadOperation(bool isNavigation) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();

        public bool IsNavigation { get; } = isNavigation;

        public CancellationToken Token => _cancellation.Token;

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished.
            }
        }

        public void Dispose()
        {
            _cancellation.Dispose();
        }
    }
}
