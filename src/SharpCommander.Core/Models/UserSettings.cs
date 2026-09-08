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
    /// The tabs of the last session, both panes together. Empty on a first run or after an upgrade, in which
    /// case each pane starts with a single tab on its last folder.
    /// </summary>
    public List<TabState> Tabs { get; set; } = [];

    /// <summary>Saved SFTP servers. Passwords are never here: they live in the platform keychain.</summary>
    public List<SftpSite> SftpSites { get; set; } = [];

    /// <summary>
    /// Gets or sets the theme: "System", "Light" or "Dark".
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>Interface language: a culture name ("en", "es") or "System" to follow the operating system.</summary>
    public string Language { get; set; } = "System";

    /// <summary>
    /// Whether to look for a new release on startup. It contacts github.com, so it is a setting rather than a
    /// given; the Help menu can always check on demand.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When the last automatic check ran, so startup asks at most once a day.</summary>
    public DateTime? LastUpdateCheck { get; set; }

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
