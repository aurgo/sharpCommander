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
    private readonly FileSystemWatcherService _watcher;
    private bool _disposed;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _displayPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileSystemEntry> _entries = [];

    [ObservableProperty]
    private FileSystemEntry? _selectedEntry;

    [ObservableProperty]
    private ObservableCollection<FileSystemEntry> _selectedEntries = [];

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRootView;

    public FilePanelViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
        _watcher = new FileSystemWatcherService();
        _watcher.Changed += OnFileSystemChanged;
    }

    public async Task InitializeAsync(string? path = null)
    {
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
                CurrentPath = string.Empty;
                DisplayPath = "Computer";
                IsRootView = true;
                UpdateStatus(drives.Count, 0, 0);
                return;
            }

            if (_fileSystemService.IsDirectory(path))
            {
                var entries = await _fileSystemService.GetEntriesAsync(path);
                Entries = new ObservableCollection<FileSystemEntry>(entries);
                CurrentPath = path;
                DisplayPath = path;
                IsRootView = false;
                
                _watcher.Start(path);
                
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
            case FileSystemEntryType.File:
                await _fileSystemService.OpenWithDefaultAsync(SelectedEntry.FullPath);
                break;
        }
    }

    public IReadOnlyList<FileSystemEntry> GetSelectedItems()
    {
        return SelectedEntries.Count > 0 
            ? SelectedEntries.ToList() 
            : SelectedEntry is not null 
                ? [SelectedEntry] 
                : [];
    }

    private void UpdateStatus(int directories, int files, long totalSize)
    {
        var sizeText = FileSizeFormatter.Format(totalSize);
        StatusText = $"{directories} folders, {files} files ({sizeText})";
    }

    private async void OnFileSystemChanged(object? sender, FileSystemChangedEventArgs e)
    {
        // Refresh the view when file system changes
        await RefreshAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watcher.Changed -= OnFileSystemChanged;
        _watcher.Dispose();
        _disposed = true;
    }
}
