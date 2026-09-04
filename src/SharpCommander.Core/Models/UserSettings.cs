namespace SharpCommander.Core.Models;

/// <summary>
/// User settings and preferences, persisted as JSON.
/// </summary>
public sealed class UserSettings
{
    /// <summary>
    /// Gets or sets the list of favorite directories.
    /// </summary>
    public List<FavoriteItem> Favorites { get; set; } = [];

    /// <summary>
    /// Gets or sets the navigation history.
    /// </summary>
    public List<NavigationHistoryItem> NavigationHistory { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of history items to keep.
    /// </summary>
    public int MaxHistoryItems { get; set; } = 50;

    /// <summary>
    /// Gets or sets the last left panel path (empty for the Computer view).
    /// </summary>
    public string? LastLeftPanelPath { get; set; }

    /// <summary>
    /// Gets or sets the last right panel path (empty for the Computer view).
    /// </summary>
    public string? LastRightPanelPath { get; set; }

    /// <summary>
    /// Gets or sets the theme: "System", "Light" or "Dark".
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Gets or sets whether hidden entries (Hidden attribute, dotfiles on Unix) are listed.
    /// </summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>
    /// Gets or sets the sort column of the file lists: "Name", "Size" or "Modified".
    /// </summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>
    /// Gets or sets the sort direction: "Ascending" or "Descending".
    /// </summary>
    public string SortDirection { get; set; } = "Ascending";

    /// <summary>
    /// Gets or sets whether the favorites panel is shown.
    /// </summary>
    public bool FavoritesPanelVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets the last window width, or null when never saved.
    /// </summary>
    public double? WindowWidth { get; set; }

    /// <summary>
    /// Gets or sets the last window height, or null when never saved.
    /// </summary>
    public double? WindowHeight { get; set; }

    /// <summary>
    /// Gets or sets the last window state: "Normal" or "Maximized"; null when never saved.
    /// </summary>
    public string? WindowState { get; set; }
}
