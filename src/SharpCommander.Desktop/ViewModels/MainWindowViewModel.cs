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

    public string Title => "SharpCommander - File Manager";
    
    public string Version => "2.0.0";

    public MainWindowViewModel(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService;
        _leftPanel = new FilePanelViewModel(fileSystemService);
        _rightPanel = new FilePanelViewModel(fileSystemService);
        _activePanel = _leftPanel;
    }

    public async Task InitializeAsync()
    {
        var homeDir = _fileSystemService.GetDefaultDirectory();
        
        await Task.WhenAll(
            LeftPanel.InitializeAsync(homeDir),
            RightPanel.InitializeAsync(homeDir)
        );
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
    private void ShowAbout()
    {
        // This will be handled in the View layer
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
        LeftPanel.Dispose();
        RightPanel.Dispose();
    }
}
