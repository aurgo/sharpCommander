namespace SharpCommander.Core.Models;

/// <summary>
/// A tab as written to the settings: which pane it belongs to, the folder it shows and whether it is pinned.
/// Enough to rebuild the tab bar on the next start; nothing about the panels themselves is persisted.
/// </summary>
public sealed class TabState
{
    /// <summary>"Left" or "Right". A string so an unknown value degrades to the left pane instead of throwing.</summary>
    public string Side { get; set; } = "Left";

    /// <summary>The folder, or empty for the volume list.</summary>
    public string Path { get; set; } = string.Empty;

    public bool IsPinned { get; set; }

    /// <summary>True for the tab its pane was showing when the session ended.</summary>
    public bool IsCurrent { get; set; }
}
