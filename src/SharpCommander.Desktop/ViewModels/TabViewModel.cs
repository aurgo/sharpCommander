using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// ViewModel for a single tab containing left and right panels.
/// </summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _title = "New Tab";

    [ObservableProperty]
    private FilePanelViewModel _leftPanel;

    [ObservableProperty]
    private FilePanelViewModel _rightPanel;

    [ObservableProperty]
    private FilePanelViewModel? _activePanel;

    public TabViewModel(IFileSystemService fileSystemService, ISettingsService settingsService)
    {
        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _leftPanel = new FilePanelViewModel(fileSystemService, settingsService);
        _rightPanel = new FilePanelViewModel(fileSystemService, settingsService);
        _activePanel = _leftPanel;
    }

    public async Task InitializeAsync(string? leftPath = null, string? rightPath = null)
    {
        var defaultPath = _fileSystemService.GetDefaultDirectory();
        
        await Task.WhenAll(
            LeftPanel.InitializeAsync(leftPath ?? defaultPath),
            RightPanel.InitializeAsync(rightPath ?? defaultPath)
        );

        // Update title based on the active panel's path
        UpdateTitle();
    }

    public void SetActivePanel(FilePanelViewModel panel)
    {
        ActivePanel = panel;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        if (ActivePanel != null && !string.IsNullOrEmpty(ActivePanel.CurrentPath))
        {
            var pathParts = ActivePanel.CurrentPath.Split(Path.DirectorySeparatorChar);
            Title = pathParts.Length > 0 ? pathParts[^1] : "Tab";
        }
        else
        {
            Title = "Computer";
        }
    }

    public void Dispose()
    {
        LeftPanel.Dispose();
        RightPanel.Dispose();
    }
}
