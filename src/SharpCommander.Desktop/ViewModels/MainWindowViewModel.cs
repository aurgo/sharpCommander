using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Main window ViewModel coordinating the dual-pane file manager.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;

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

    public string Title => "SharpCommander - File Manager";

    public string Version => "2.0.0";

    public MainWindowViewModel(IFileSystemService fileSystemService, ISettingsService settingsService, IDialogService dialogService, IClipboardService clipboardService)
    {
        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _leftPanel = new FilePanelViewModel(fileSystemService, settingsService, dialogService, clipboardService);
        _rightPanel = new FilePanelViewModel(fileSystemService, settingsService, dialogService, clipboardService);
        _activePanel = _leftPanel;

        // Subscribe to favorites changes to sync both panels
        _leftPanel.FavoritesChanged += OnFavoritesChanged;
        _rightPanel.FavoritesChanged += OnFavoritesChanged;

        // Initialize with a default tab
        var defaultTab = new TabViewModel(fileSystemService, settingsService, dialogService, clipboardService);
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
        if (CurrentTab != null)
        {
            CurrentTab.ActivePanel = panel;
        }
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
        ActivePanel.RequestFocus();
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
        ActivePanel.RequestFocus();
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

        var firstSelected = selectedItems.FirstOrDefault();
        var currentIndex = firstSelected != null ? ActivePanel.FilteredEntries.IndexOf(firstSelected) : -1;

        await ExecuteFileOperationAsync(
            "Deleting",
            selectedItems,
            async (item, progress) =>
            {
                await _fileSystemService.DeleteAsync(item.FullPath, progress);
            }
        );

        await ActivePanel.RefreshAsync();

        if (ActivePanel.FilteredEntries.Count > 0 && currentIndex >= 0)
        {
            var targetIndex = Math.Min(currentIndex, ActivePanel.FilteredEntries.Count - 1);
            var entryToSelect = ActivePanel.FilteredEntries[targetIndex];

            ActivePanel.SelectedEntry = entryToSelect;
            ActivePanel.SelectedEntries.Clear();
            ActivePanel.SelectedEntries.Add(entryToSelect);
        }

        ActivePanel.RequestFocus();
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

        var newFolderName = await _dialogService.ShowInputDialogAsync(
            "New Folder",
            "Enter folder name:",
            "New Folder");

        if (string.IsNullOrWhiteSpace(newFolderName))
        {
            return;
        }

        var newFolderPath = Path.Combine(ActivePanel.CurrentPath, newFolderName);

        if (Directory.Exists(newFolderPath))
        {
            StatusMessage = $"Folder '{newFolderName}' already exists.";
            return;
        }

        try
        {
            await _fileSystemService.CreateDirectoryAsync(newFolderPath);
            await ActivePanel.RefreshAsync();
            
            var newEntry = ActivePanel.FilteredEntries.FirstOrDefault(e => e.FullPath == newFolderPath);
            if (newEntry != null)
            {
                ActivePanel.SelectedEntry = newEntry;
                ActivePanel.SelectedEntries.Clear();
                ActivePanel.SelectedEntries.Add(newEntry);
            }

            ActivePanel.RequestFocus();
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
        if (ActivePanel == null) return;
        await ActivePanel.RenameSelectedCommand.ExecuteAsync(null);
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
        var newTab = new TabViewModel(_fileSystemService, _settingsService, _dialogService, _clipboardService);
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
        if (ActivePanel == null) return;
        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0) return;

        _clipboardService.Copy(selectedItems);
        StatusMessage = $"Copied {selectedItems.Count} item(s) to clipboard";
    }

    [RelayCommand]
    private void CutToClipboard()
    {
        if (ActivePanel == null) return;
        var selectedItems = ActivePanel.GetSelectedItems();
        if (selectedItems.Count == 0) return;

        _clipboardService.Cut(selectedItems);
        StatusMessage = $"Cut {selectedItems.Count} item(s) to clipboard";
    }

    [RelayCommand]
    private async Task PasteFromClipboardAsync()
    {
        var items = _clipboardService.GetItems();
        if (ActivePanel == null || items.Count == 0)
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

        if (_clipboardService.IsCutMode)
        {
            await ExecuteFileOperationAsync(
                "Moving",
                items,
                async (item, progress) =>
                {
                    await _fileSystemService.MoveAsync(item.FullPath, destination, true, progress);
                }
            );
            _clipboardService.Clear();
        }
        else
        {
            await ExecuteFileOperationAsync(
                "Pasting",
                items,
                async (item, progress) =>
                {
                    await _fileSystemService.CopyAsync(item.FullPath, destination, true, progress);
                }
            );
        }

        await ActivePanel.RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowAdvancedSearchAsync()
    {
        if (ActivePanel == null) return;
        await _dialogService.ShowAdvancedSearchDialogAsync(ActivePanel.CurrentPath);
    }

    [RelayCommand]
    private async Task ShowMassRenameAsync()
    {
        if (ActivePanel == null) return;

        // Get actual file paths from selected entries
        var selectedFiles = ActivePanel.SelectedEntries
            .Where(e => e.EntryType == FileSystemEntryType.File)
            .Select(e => e.FullPath)
            .ToList();

        if (selectedFiles.Count == 0)
        {
            StatusMessage = "No files selected for mass rename";
            return;
        }

        await _dialogService.ShowMassRenameDialogAsync(selectedFiles);
    }

    [RelayCommand]
    private async Task CalculateHashAsync()
    {
        if (ActivePanel == null) return;
        
        var selectedItem = ActivePanel.SelectedEntry;
        if (selectedItem == null)
        {
            StatusMessage = "No file selected";
            return;
        }

        if (selectedItem.EntryType != FileSystemEntryType.File)
        {
            StatusMessage = "Please select a file to calculate hash";
            return;
        }

        try
        {
            await _dialogService.ShowHashDialogAsync(selectedItem.FullPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error showing hash dialog: {ex.Message}";
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
