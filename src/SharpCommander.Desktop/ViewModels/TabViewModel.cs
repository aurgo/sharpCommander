using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// One tab: a folder remembered for one of the two panes. A tab belongs to a fixed <see cref="Side"/> — the pane
/// it was opened from — which decides where it sits in the tab bar and which panel it drives. The panels
/// themselves live in <see cref="MainWindowViewModel"/> and are shared by every tab, so activating a tab moves
/// only its own side and leaves the opposite pane exactly where it was.
/// </summary>
public sealed partial class TabViewModel : ObservableObject
{
    private const string ComputerTitle = "Computer";

    /// <summary>The folder this tab shows on its side; empty means the volume list.</summary>
    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _title;

    /// <summary>Whether this is the tab currently on screen; the tab bar highlights it.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// A pinned tab keeps its folder: navigating the pane opens a new tab instead of moving this one, and it
    /// cannot be closed. Use it to keep a folder one click away.
    /// </summary>
    [ObservableProperty]
    private bool _isPinned;

    public TabViewModel(PanelSide side, string? path = null)
    {
        Side = side;
        _path = path ?? string.Empty;
        _title = TitleFor(_path);
    }

    /// <summary>The pane this tab was opened from. Fixed for the life of the tab.</summary>
    public PanelSide Side { get; }

    /// <summary>The tab title for a folder path: its last segment, or the path itself for a root.</summary>
    internal static string TitleFor(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ComputerTitle;
        }

        // Fully qualified: this class has its own Path property.
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    partial void OnPathChanged(string value)
    {
        Title = TitleFor(value);
    }
}
