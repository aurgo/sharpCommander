using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace SharpCommander.Desktop.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;

    [ObservableProperty]
    private string _searchPath = string.Empty;

    [ObservableProperty]
    private string _fileNamePattern = "*";

    [ObservableProperty]
    private string _contentPattern = string.Empty;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<FileSystemEntry> _results = [];

    [ObservableProperty]
    private string _statusText = "Ready";

    public SearchViewModel(IFileSystemService fileSystemService, string initialPath)
    {
        _fileSystemService = fileSystemService;
        SearchPath = initialPath;
    }

    private const int MaxSearchDepth = 32;
    private const long MaxFileSizeForContentSearch = 10 * 1024 * 1024; // 10 MB

    [RelayCommand]
    private async Task StartSearchAsync()
    {
        if (IsSearching) return;

        Results.Clear();
        IsSearching = true;
        StatusText = "Searching...";

        try
        {
            await SearchRecursiveAsync(SearchPath, 0);
            StatusText = $"Search completed. Found {Results.Count} items.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task SearchRecursiveAsync(string path, int depth)
    {
        if (depth > MaxSearchDepth) return;

        try
        {
            var entries = await _fileSystemService.GetEntriesAsync(path);
            foreach (var entry in entries)
            {
                if (entry.EntryType == FileSystemEntryType.ParentDirectory) continue;

                bool matchesName;
                if (UseRegex)
                {
                    matchesName = Regex.IsMatch(entry.Name, FileNamePattern, RegexOptions.IgnoreCase);
                }
                else
                {
                    matchesName = entry.Name.Contains(FileNamePattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase);
                }

                if (matchesName)
                {
                    if (string.IsNullOrEmpty(ContentPattern))
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Results.Add(entry));
                    }
                    else if (entry.EntryType == FileSystemEntryType.File)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(entry.FullPath);
                            if (fileInfo.Length <= MaxFileSizeForContentSearch)
                            {
                                var content = await File.ReadAllTextAsync(entry.FullPath);
                                if (content.Contains(ContentPattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Results.Add(entry));
                                }
                            }
                        }
                        catch { /* Ignore file access errors */ }
                    }
                }

                if (entry.EntryType == FileSystemEntryType.Directory)
                {
                    await SearchRecursiveAsync(entry.FullPath, depth + 1);
                }
            }
        }
        catch { /* Ignore directory access errors */ }
    }
}
