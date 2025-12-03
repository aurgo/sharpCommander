using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Main window ViewModel coordinating the dual-pane file manager.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<TabViewModel> _tabs = [];

    [ObservableProperty]
    private TabViewModel? _currentTab;

    [ObservableProperty]
    private FilePanelViewModel _leftPanel;

    [ObservableProperty]
    private FilePanelViewModel _rightPanel;

    [ObservableProperty]
    private FilePanelViewModel? _activePanel;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isOperationInProgress;

    [ObservableProperty]
    private double _operationProgress;

    [ObservableProperty]
    private string _currentOperation = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesPanel = true;

    [ObservableProperty]
    private string _newItemName = string.Empty;

    // Clipboard-related properties
    private List<FileSystemEntry> _clipboardItems = new();
    private bool _clipboardCutMode = false;

    public string Title => "SharpCommander - File Manager";
    
    public string Version => "2.0.0";

    public MainWindowViewModel(IFileSystemService fileSystemService, ISettingsService settingsService)
    {
        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _leftPanel = new FilePanelViewModel(fileSystemService, settingsService);
        _rightPanel = new FilePanelViewModel(fileSystemService, settingsService);
        _activePanel = _leftPanel;
        
        // Subscribe to favorites changes to sync both panels
        _leftPanel.FavoritesChanged += OnFavoritesChanged;
        _rightPanel.FavoritesChanged += OnFavoritesChanged;

        // Initialize with a default tab
        var defaultTab = new TabViewModel(fileSystemService, settingsService);
        Tabs.Add(defaultTab);
        CurrentTab = defaultTab;
    }

    partial void OnCurrentTabChanged(TabViewModel? value)
    {
        if (value != null)
        {
            // Update the main panels when tab changes
            LeftPanel = value.LeftPanel;
            RightPanel = value.RightPanel;
            ActivePanel = value.ActivePanel;
        }
    }

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        // When one panel changes favorites, update the other panel
        if (sender == LeftPanel)
        {
            RightPanel.LoadFavorites();
        }
        else if (sender == RightPanel)
        {
            LeftPanel.LoadFavorites();
        }
    }

    public async Task InitializeAsync()
    {
        // Load settings from disk first (includes favorites and history)
        await _settingsService.LoadAsync();
        
        var leftPath = _settingsService.Settings.LastLeftPanelPath ?? _fileSystemService.GetDefaultDirectory();
        var rightPath = _settingsService.Settings.LastRightPanelPath ?? _fileSystemService.GetDefaultDirectory();
        
        // Initialize the default tab
        if (CurrentTab != null)
        {
            await CurrentTab.InitializeAsync(leftPath, rightPath);
            LeftPanel = CurrentTab.LeftPanel;
            RightPanel = CurrentTab.RightPanel;
            ActivePanel = CurrentTab.ActivePanel;
        }
    }

    public async Task SaveStateAsync()
    {
        _settingsService.Settings.LastLeftPanelPath = LeftPanel.CurrentPath;
        _settingsService.Settings.LastRightPanelPath = RightPanel.CurrentPath;
        await _settingsService.SaveAsync();
    }

    public void SetActivePanel(FilePanelViewModel panel)
    {
        ActivePanel = panel;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (ActivePanel is null)
        {
            return;
        }

        var sourceItems = ActivePanel.GetSelectedItems();
        if (sourceItems.Count == 0)
        {
            return;
        }

        var destinationPanel = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        var destination = destinationPanel.CurrentPath;

        if (string.IsNullOrEmpty(destination))
        {
            StatusMessage = "Cannot copy to root view";
            return;
        }

        await ExecuteFileOperationAsync(
            "Copying",
            sourceItems,
            async (item, progress) =>
            {
                await _fileSystemService.CopyAsync(item.FullPath, destination, true, progress);
            }
        );

        await destinationPanel.RefreshAsync();
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        if (ActivePanel is null)
        {
            return;
        }

        var sourceItems = ActivePanel.GetSelectedItems();
        if (sourceItems.Count == 0)
        {
            return;
        }

        var destinationPanel = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        var destination = destinationPanel.CurrentPath;

        if (string.IsNullOrEmpty(destination))
        {
            StatusMessage = "Cannot move to root view";
            return;
        }

        await ExecuteFileOperationAsync(
            "Moving",
            sourceItems,
            async (item, progress) =>
            {
                await _fileSystemService.MoveAsync(item.FullPath, destination, true, progress);
            }
        );

        await Task.WhenAll(
            ActivePanel.RefreshAsync(),
            destinationPanel.RefreshAsync()
        );
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (ActivePanel is null)
        {
            return;
        }

        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        await ExecuteFileOperationAsync(
            "Deleting",
            selectedItems,
            async (item, progress) =>
            {
                await _fileSystemService.DeleteAsync(item.FullPath, progress);
            }
        );

        await ActivePanel.RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await Task.WhenAll(
            LeftPanel.RefreshAsync(),
            RightPanel.RefreshAsync()
        );
    }

    [RelayCommand]
    private void ToggleFavoritesPanel()
    {
        ShowFavoritesPanel = !ShowFavoritesPanel;
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (ActivePanel is null || string.IsNullOrEmpty(ActivePanel.CurrentPath))
        {
            StatusMessage = "Cannot create folder in root view";
            return;
        }

        // Default name - in a real app would show a dialog
        var newFolderName = "New Folder";
        var newFolderPath = Path.Combine(ActivePanel.CurrentPath, newFolderName);
        
        // Find unique name
        var counter = 1;
        while (Directory.Exists(newFolderPath))
        {
            newFolderName = $"New Folder ({counter++})";
            newFolderPath = Path.Combine(ActivePanel.CurrentPath, newFolderName);
        }

        try
        {
            await _fileSystemService.CreateDirectoryAsync(newFolderPath);
            await ActivePanel.RefreshAsync();
            StatusMessage = $"Created folder: {newFolderName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (ActivePanel?.SelectedEntry is null)
        {
            StatusMessage = "No item selected for rename";
            return;
        }

        // In a real app would show a rename dialog
        StatusMessage = "Rename: Press F2 or right-click for rename option";
    }

    [RelayCommand]
    private async Task ViewAsync()
    {
        if (ActivePanel?.SelectedEntry?.EntryType == FileSystemEntryType.File)
        {
            await _fileSystemService.OpenWithDefaultAsync(ActivePanel.SelectedEntry.FullPath);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (ActivePanel?.SelectedEntry?.EntryType == FileSystemEntryType.File)
        {
            // Open with default editor
            await _fileSystemService.OpenWithDefaultAsync(ActivePanel.SelectedEntry.FullPath);
        }
    }

    [RelayCommand]
    private async Task SyncPanelsAsync()
    {
        if (ActivePanel is null)
        {
            return;
        }

        var targetPanel = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        await targetPanel.NavigateToCommand.ExecuteAsync(ActivePanel.CurrentPath);
    }

    [RelayCommand]
    private async Task SwapPanelsAsync()
    {
        var leftPath = LeftPanel.CurrentPath;
        var rightPath = RightPanel.CurrentPath;

        await Task.WhenAll(
            LeftPanel.NavigateToCommand.ExecuteAsync(rightPath),
            RightPanel.NavigateToCommand.ExecuteAsync(leftPath)
        );
    }

    [RelayCommand]
    private async Task NewTabAsync()
    {
        var newTab = new TabViewModel(_fileSystemService, _settingsService);
        await newTab.InitializeAsync();
        Tabs.Add(newTab);
        CurrentTab = newTab;
        StatusMessage = "New tab created";
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        if (tab == null || Tabs.Count <= 1)
        {
            StatusMessage = "Cannot close the last tab";
            return;
        }

        var index = Tabs.IndexOf(tab);
        tab.Dispose();
        Tabs.Remove(tab);

        // Select the previous tab or the first tab
        if (CurrentTab == tab)
        {
            CurrentTab = index > 0 ? Tabs[index - 1] : Tabs[0];
        }

        StatusMessage = "Tab closed";
    }

    [RelayCommand]
    private void ShowAbout()
    {
        // This will be handled in the View layer
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (ActivePanel?.FilteredEntries == null)
        {
            return;
        }

        ActivePanel.SelectedEntries.Clear();
        foreach (var entry in ActivePanel.FilteredEntries)
        {
            // Don't select the parent directory entry
            if (entry.EntryType != FileSystemEntryType.ParentDirectory)
            {
                ActivePanel.SelectedEntries.Add(entry);
            }
        }
        StatusMessage = $"Selected {ActivePanel.SelectedEntries.Count} item(s)";
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (ActivePanel == null)
        {
            return;
        }

        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        _clipboardItems = selectedItems.ToList();
        _clipboardCutMode = false;
        StatusMessage = $"Copied {_clipboardItems.Count} item(s) to clipboard";
    }

    [RelayCommand]
    private void CutToClipboard()
    {
        if (ActivePanel == null)
        {
            return;
        }

        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        _clipboardItems = selectedItems.ToList();
        _clipboardCutMode = true;
        StatusMessage = $"Cut {_clipboardItems.Count} item(s) to clipboard";
    }

    [RelayCommand]
    private async Task PasteFromClipboardAsync()
    {
        if (ActivePanel == null || _clipboardItems.Count == 0)
        {
            StatusMessage = "Clipboard is empty";
            return;
        }

        if (string.IsNullOrEmpty(ActivePanel.CurrentPath))
        {
            StatusMessage = "Cannot paste to root view";
            return;
        }

        var destination = ActivePanel.CurrentPath;

        if (_clipboardCutMode)
        {
            // Move operation
            await ExecuteFileOperationAsync(
                "Moving",
                _clipboardItems,
                async (item, progress) =>
                {
                    await _fileSystemService.MoveAsync(item.FullPath, destination, true, progress);
                }
            );
            _clipboardItems.Clear();
            _clipboardCutMode = false;
        }
        else
        {
            // Copy operation
            await ExecuteFileOperationAsync(
                "Pasting",
                _clipboardItems,
                async (item, progress) =>
                {
                    await _fileSystemService.CopyAsync(item.FullPath, destination, true, progress);
                }
            );
        }

        await ActivePanel.RefreshAsync();
    }

    [RelayCommand]
    private void ShowAdvancedSearch()
    {
        StatusMessage = "Advanced search dialog would open here";
        // TODO: Implement advanced search dialog with regex and content search
    }

    [RelayCommand]
    private void ShowMassRename()
    {
        if (ActivePanel == null)
        {
            return;
        }

        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "No files selected for mass rename";
            return;
        }

        StatusMessage = $"Mass rename dialog would open for {selectedItems.Count} item(s)";
        // TODO: Implement mass rename dialog
    }

    [RelayCommand]
    private async Task CalculateHashAsync()
    {
        if (ActivePanel?.SelectedEntry == null)
        {
            StatusMessage = "No file selected";
            return;
        }

        if (ActivePanel.SelectedEntry.EntryType != FileSystemEntryType.File)
        {
            StatusMessage = "Please select a file to calculate hash";
            return;
        }

        try
        {
            IsOperationInProgress = true;
            StatusMessage = "Calculating hashes...";

            var filePath = ActivePanel.SelectedEntry.FullPath;
            
            await Task.Run(async () =>
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                md5.TransformFinalBlock(buffer, 0, 0);
                sha1.TransformFinalBlock(buffer, 0, 0);
                sha256.TransformFinalBlock(buffer, 0, 0);

                var md5Hash = BitConverter.ToString(md5.Hash!).Replace("-", "");
                var sha1Hash = BitConverter.ToString(sha1.Hash!).Replace("-", "");
                var sha256Hash = BitConverter.ToString(sha256.Hash!).Replace("-", "");

                StatusMessage = $"MD5: {md5Hash} | SHA1: {sha1Hash} | SHA256: {sha256Hash}";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error calculating hash: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }

    private async Task ExecuteFileOperationAsync(
        string operationName,
        IReadOnlyList<FileSystemEntry> items,
        Func<FileSystemEntry, IProgress<FileOperationProgress>, Task> operation)
    {
        IsOperationInProgress = true;
        CurrentOperation = operationName;
        
        try
        {
            var progress = new Progress<FileOperationProgress>(p =>
            {
                StatusMessage = $"{operationName}: {p.CurrentFile}";
                OperationProgress = p.PercentComplete;
            });

            var processedCount = 0;
            foreach (var item in items)
            {
                StatusMessage = $"{operationName}: {item.Name}";
                OperationProgress = (double)processedCount / items.Count * 100;

                try
                {
                    await operation(item, progress);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error {operationName.ToLower()} {item.Name}: {ex.Message}";
                }

                processedCount++;
            }

            StatusMessage = $"{operationName} completed: {processedCount} item(s)";
        }
        finally
        {
            IsOperationInProgress = false;
            OperationProgress = 0;
            CurrentOperation = string.Empty;
        }
    }

    public void Dispose()
    {
        // Dispose all tabs
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
        Tabs.Clear();

        // Note: LeftPanel and RightPanel are now managed by tabs
        // But we should still unsubscribe from their events if needed
        if (LeftPanel != null)
        {
            LeftPanel.FavoritesChanged -= OnFavoritesChanged;
        }
        if (RightPanel != null)
        {
            RightPanel.FavoritesChanged -= OnFavoritesChanged;
        }
    }
}
