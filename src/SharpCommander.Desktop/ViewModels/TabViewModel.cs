using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SharpCommander.Core.Interfaces;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// One tab: a left and a right panel and which of them is active. The title follows the active panel's folder.
/// Panels are created here and only here, and are disposed with the tab.
/// </summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private const string ComputerTitle = "Computer";

    private readonly IFileSystemService _fileSystemService;
    private bool _disposed;

    [ObservableProperty]
    private string _title = ComputerTitle;

    [ObservableProperty]
    private FilePanelViewModel _activePanel;

    public FilePanelViewModel LeftPanel { get; }

    public FilePanelViewModel RightPanel { get; }

    public TabViewModel(IFileSystemService fileSystemService, ISettingsService settingsService, IDialogService dialogService, IClipboardService clipboardService, IFileOperationsService fileOperationsService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;

        LeftPanel = new FilePanelViewModel(fileSystemService, settingsService, dialogService, clipboardService, fileOperationsService);
        RightPanel = new FilePanelViewModel(fileSystemService, settingsService, dialogService, clipboardService, fileOperationsService);
        _activePanel = LeftPanel;

        LeftPanel.PropertyChanged += OnPanelPropertyChanged;
        RightPanel.PropertyChanged += OnPanelPropertyChanged;
        UpdateTitle();
    }

    /// <summary>Navigates both panels to their starting folders (the default directory when null).</summary>
    public async Task InitializeAsync(string? leftPath = null, string? rightPath = null)
    {
        var defaultPath = _fileSystemService.GetDefaultDirectory();

        await Task.WhenAll(
            LeftPanel.InitializeAsync(leftPath ?? defaultPath),
            RightPanel.InitializeAsync(rightPath ?? defaultPath));

        UpdateTitle();
    }

    /// <summary>Makes one of this tab's panels the active one; other panels are ignored.</summary>
    public void SetActivePanel(FilePanelViewModel panel)
    {
        if (ReferenceEquals(panel, LeftPanel) || ReferenceEquals(panel, RightPanel))
        {
            ActivePanel = panel;
        }
    }

    /// <summary>Gets the panel opposite to <paramref name="panel"/> (the left one for anything else).</summary>
    public FilePanelViewModel OtherPanel(FilePanelViewModel panel)
    {
        return ReferenceEquals(panel, LeftPanel) ? RightPanel : LeftPanel;
    }

    /// <summary>The tab title for a folder path: its last segment, or the path itself for a root.</summary>
    internal static string TitleFor(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ComputerTitle;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    partial void OnActivePanelChanged(FilePanelViewModel value)
    {
        UpdateTitle();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ActivePanel) && e.PropertyName is nameof(FilePanelViewModel.CurrentPath) or nameof(FilePanelViewModel.IsRootView))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        Title = ActivePanel.IsRootView ? ComputerTitle : TitleFor(ActivePanel.CurrentPath);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LeftPanel.PropertyChanged -= OnPanelPropertyChanged;
        RightPanel.PropertyChanged -= OnPanelPropertyChanged;
        LeftPanel.Dispose();
        RightPanel.Dispose();
    }
}
