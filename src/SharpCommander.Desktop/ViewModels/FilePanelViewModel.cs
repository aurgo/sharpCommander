using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// ViewModel for a single file panel.
/// </summary>
public sealed partial class FilePanelViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly FileSystemWatcherService _watcher;
    private bool _disposed;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _displayPath = string.Empty;

    [ObservableProperty]
    private string _editablePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileSystemEntry> _entries = [];

    [ObservableProperty]
    private ObservableCollection<FileSystemEntry> _filteredEntries = [];

    [ObservableProperty]
    private FileSystemEntry? _selectedEntry;

    public ObservableCollection<FileSystemEntry> SelectedEntries { get; } = [];

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRootView;

    [ObservableProperty]
    private ObservableCollection<NavigationHistoryItem> _navigationHistory = [];

    [ObservableProperty]
    private NavigationHistoryItem? _selectedHistoryItem;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private ObservableCollection<FavoriteItem> _favorites = [];

    public FilePanelViewModel(IFileSystemService fileSystemService, ISettingsService settingsService, IDialogService dialogService, IClipboardService clipboardService)
    {
        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _watcher = new FileSystemWatcherService();
        _watcher.Changed += OnFileSystemChanged;
        
        // Note: LoadFavorites() and LoadHistory() are called later in InitializeAsync()
        // after settings have been loaded from disk
    }

    public void LoadFavorites()
    {
        Favorites.Clear();
        foreach (var fav in _settingsService.Settings.Favorites.OrderBy(f => f.Order))
        {
            Favorites.Add(fav);
        }
    }

    private void LoadHistory()
    {
        NavigationHistory.Clear();
        foreach (var item in _settingsService.GetRecentHistory(30))
        {
            NavigationHistory.Add(item);
        }
    }

    public async Task InitializeAsync(string? path = null)
    {
        // Load favorites and history now that settings are loaded from disk
        LoadFavorites();
        LoadHistory();
        
        var initialPath = path ?? _fileSystemService.GetDefaultDirectory();
        await NavigateToAsync(initialPath);
    }

    [RelayCommand]
    private async Task NavigateToAsync(string path)
    {
        try
        {
            IsLoading = true;
            _watcher.Stop();

            if (string.IsNullOrEmpty(path) || !_fileSystemService.Exists(path))
            {
                // Show drives
                var drives = await _fileSystemService.GetDrivesAsync();
                Entries = new ObservableCollection<FileSystemEntry>(drives);
                FilteredEntries = new ObservableCollection<FileSystemEntry>(drives);
                CurrentPath = string.Empty;
                DisplayPath = "Computer";
                EditablePath = string.Empty;
                IsRootView = true;
                IsFavorite = false;
                UpdateStatus(drives.Count, 0, 0);
                return;
            }

            if (_fileSystemService.IsDirectory(path))
            {
                var entries = await _fileSystemService.GetEntriesAsync(path);
                Entries = new ObservableCollection<FileSystemEntry>(entries);
                ApplySearchFilter();
                CurrentPath = path;
                DisplayPath = path;
                EditablePath = path;
                IsRootView = false;
                IsFavorite = _settingsService.IsFavorite(path);
                
                _watcher.Start(path);
                
                // Add to history
                await _settingsService.AddToHistoryAsync(path);
                LoadHistory();
                
                var dirCount = entries.Count(e => e.EntryType == FileSystemEntryType.Directory);
                var fileCount = entries.Count(e => e.EntryType == FileSystemEntryType.File);
                var totalSize = entries.Where(e => e.EntryType == FileSystemEntryType.File).Sum(e => e.Size);
                UpdateStatus(dirCount, fileCount, totalSize);
            }
            else
            {
                // It's a file - open it
                await _fileSystemService.OpenWithDefaultAsync(path);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToPathAsync()
    {
        if (!string.IsNullOrEmpty(EditablePath))
        {
            await NavigateToAsync(EditablePath);
        }
    }

    [RelayCommand]
    private async Task NavigateToHistoryItemAsync(NavigationHistoryItem? item)
    {
        if (item is not null)
        {
            await NavigateToAsync(item.Path);
        }
    }

    [RelayCommand]
    private async Task NavigateToFavoriteAsync(FavoriteItem? favorite)
    {
        if (favorite is not null)
        {
            await NavigateToAsync(favorite.Path);
        }
    }

    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            return;
        }

        var parentPath = _fileSystemService.GetParentPath(CurrentPath);
        await NavigateToAsync(parentPath ?? string.Empty);
    }

    [RelayCommand]
    private async Task NavigateToRootAsync()
    {
        await NavigateToAsync(string.Empty);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await NavigateToAsync(CurrentPath);
    }

    [RelayCommand]
    private async Task OpenSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        switch (SelectedEntry.EntryType)
        {
            case FileSystemEntryType.Directory:
            case FileSystemEntryType.Drive:
                await NavigateToAsync(SelectedEntry.FullPath);
                break;
            case FileSystemEntryType.ParentDirectory:
                await NavigateUpAsync();
                break;
            case FileSystemEntryType.File:
                await _fileSystemService.OpenWithDefaultAsync(SelectedEntry.FullPath);
                break;
        }
    }

    /// <summary>
    /// Event raised when favorites list changes (add/remove).
    /// </summary>
    public event EventHandler? FavoritesChanged;

    /// <summary>
    /// Event raised when the panel needs to reclaim focus.
    /// </summary>
    public event EventHandler? FocusRequested;

    public void RequestFocus()
    {
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || IsRootView)
        {
            return;
        }

        if (IsFavorite)
        {
            await _settingsService.RemoveFavoriteAsync(CurrentPath);
        }
        else
        {
            await _settingsService.AddFavoriteAsync(CurrentPath);
        }

        IsFavorite = _settingsService.IsFavorite(CurrentPath);
        LoadFavorites();
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task AddToFavoritesAsync()
    {
        if (SelectedEntry?.EntryType == FileSystemEntryType.Directory)
        {
            await _settingsService.AddFavoriteAsync(SelectedEntry.FullPath, SelectedEntry.Name);
            LoadFavorites();
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (!string.IsNullOrEmpty(CurrentPath) && !IsRootView)
        {
            await _settingsService.AddFavoriteAsync(CurrentPath);
            IsFavorite = true;
            LoadFavorites();
            FavoritesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task OpenInFileExplorerAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            return;
        }

        try
        {
            await _fileSystemService.OpenInFileExplorerAsync(CurrentPath);
        }
        catch (Exception ex)
        {
            StatusText = $"Error opening file explorer: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null || SelectedEntry.EntryType == FileSystemEntryType.ParentDirectory) return;
        try
        {
            var currentIndex = FilteredEntries.IndexOf(SelectedEntry);

            await _fileSystemService.DeleteAsync(SelectedEntry.FullPath);
            await RefreshAsync();

            if (FilteredEntries.Count > 0 && currentIndex >= 0)
            {
                var targetIndex = Math.Min(currentIndex, FilteredEntries.Count - 1);
                var entryToSelect = FilteredEntries[targetIndex];

                SelectedEntry = entryToSelect;
                SelectedEntries.Clear();
                SelectedEntries.Add(entryToSelect);
            }

            RequestFocus();
        }
        catch (Exception ex)
        {
            StatusText = $"Error deleting: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameSelectedAsync()
    {
        if (SelectedEntry is null || SelectedEntry.EntryType == FileSystemEntryType.ParentDirectory) return;
        
        var newName = await _dialogService.ShowInputDialogAsync(
            "Rename", 
            $"Enter new name for '{SelectedEntry.Name}':", 
            SelectedEntry.Name);
            
        if (!string.IsNullOrEmpty(newName) && newName != SelectedEntry.Name)
        {
            try
            {
                var oldPath = SelectedEntry.FullPath;
                var parent = System.IO.Path.GetDirectoryName(oldPath);
                var newPath = System.IO.Path.Combine(parent ?? string.Empty, newName);

                await _fileSystemService.RenameAsync(oldPath, newName);
                StatusText = $"Renamed to: {newName}";
                await RefreshAsync();
                
                var newEntry = FilteredEntries.FirstOrDefault(e => e.FullPath == newPath);
                if (newEntry != null)
                {
                    SelectedEntry = newEntry;
                    SelectedEntries.Clear();
                    SelectedEntries.Add(newEntry);
                }

                RequestFocus();
            }
            catch (Exception ex)
            {
                StatusText = $"Error renaming: {ex.Message}";
            }
        }
        else
        {
            StatusText = $"Rename cancelled (left original name or empty)";
        }
    }

    [RelayCommand]
    private async Task ShowPropertiesAsync()
    {
        if (SelectedEntry is null) return;
        await _dialogService.ShowPropertiesDialogAsync(SelectedEntry);
    }

    [RelayCommand]
    private void Cut()
    {
        var items = GetSelectedItems();
        if (items.Count == 0) return;
        _clipboardService.Cut(items);
    }

    [RelayCommand]
    private void Copy()
    {
        var items = GetSelectedItems();
        if (items.Count == 0) return;
        _clipboardService.Copy(items);
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var items = _clipboardService.GetItems();
        if (items.Count == 0) return;

        if (_clipboardService.IsCutMode)
        {
            foreach (var item in items)
            {
                await _fileSystemService.MoveAsync(item.FullPath, CurrentPath, true);
            }
            _clipboardService.Clear();
        }
        else
        {
            foreach (var item in items)
            {
                await _fileSystemService.CopyAsync(item.FullPath, CurrentPath, true);
            }
        }
        await RefreshAsync();
        RequestFocus();
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchActive = !IsSearchActive;
        if (!IsSearchActive)
        {
            SearchFilter = string.Empty;
            ApplySearchFilter();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchFilter = string.Empty;
        IsSearchActive = false;
        ApplySearchFilter();
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        // Store current selection(s) by name/path to restore later
        var selectedPath = SelectedEntry?.FullPath;
        var selectedPaths = SelectedEntries.Select(e => e.FullPath).ToList();

        if (string.IsNullOrWhiteSpace(SearchFilter))
        {
            FilteredEntries = new ObservableCollection<FileSystemEntry>(Entries);
        }
        else
        {
            var filtered = Entries.Where(e => 
                e.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));
            FilteredEntries = new ObservableCollection<FileSystemEntry>(filtered);
        }

        // Restore selection if possible
        if (selectedPath != null)
        {
            SelectedEntry = FilteredEntries.FirstOrDefault(e => e.FullPath == selectedPath);
        }

        if (selectedPaths.Count > 0)
        {
            SelectedEntries.Clear();
            foreach (var path in selectedPaths)
            {
                var entry = FilteredEntries.FirstOrDefault(e => e.FullPath == path);
                if (entry != null)
                {
                    SelectedEntries.Add(entry);
                }
            }
        }
    }

    public IReadOnlyList<FileSystemEntry> GetSelectedItems()
    {
        var items = SelectedEntries.Count > 0 
            ? SelectedEntries.ToList() 
            : SelectedEntry is not null 
                ? [SelectedEntry] 
                : [];

        return items.Where(e => e.EntryType != FileSystemEntryType.ParentDirectory).ToList();
    }

    private void UpdateStatus(int directories, int files, long totalSize)
    {
        var sizeText = FileSizeFormatter.Format(totalSize);
        StatusText = $"{directories} folders, {files} files ({sizeText})";
    }

    private System.Timers.Timer? _refreshTimer;

    private void OnFileSystemChanged(object? sender, FileSystemChangedEventArgs e)
    {
        // Debounce refreshes to avoid excessive CPU usage and UI flicker
        if (_refreshTimer == null)
        {
            _refreshTimer = new System.Timers.Timer(200);
            _refreshTimer.AutoReset = false;
            _refreshTimer.Elapsed += async (s, args) => 
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => 
                {
                    await RefreshAsync();
                });
            };
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watcher.Changed -= OnFileSystemChanged;
        _watcher.Dispose();
        
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }
        _disposed = true;
    }
}
