namespace SharpCommander.Core.Models;

/// <summary>
/// User settings and preferences.
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
    /// Gets or sets the last left panel path.
    /// </summary>
    public string? LastLeftPanelPath { get; set; }

    /// <summary>
    /// Gets or sets the last right panel path.
    /// </summary>
    public string? LastRightPanelPath { get; set; }

    /// <summary>
    /// Gets or sets the preferred theme (Light, Dark, System).
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Gets or sets whether to show hidden files.
    /// </summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>
    /// Gets or sets whether to show file extensions.
    /// </summary>
    public bool ShowFileExtensions { get; set; } = true;

    /// <summary>
    /// Gets or sets the default sort column.
    /// </summary>
    public string SortColumn { get; set; } = "Name";

    /// <summary>
    /// Gets or sets the sort direction (Ascending/Descending).
    /// </summary>
    public string SortDirection { get; set; } = "Ascending";
}
