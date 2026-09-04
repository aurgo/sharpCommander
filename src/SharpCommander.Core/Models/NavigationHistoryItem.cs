namespace SharpCommander.Core.Models;

/// <summary>
/// Represents an item in the navigation history.
/// </summary>
public sealed class NavigationHistoryItem
{
    /// <summary>
    /// Gets or sets the full path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last visited timestamp.
    /// </summary>
    public DateTime LastVisited { get; set; } = DateTime.Now;

    /// <summary>
    /// Gets or sets the visit count.
    /// </summary>
    public int VisitCount { get; set; } = 1;

    /// <summary>
    /// Returns <see cref="Path"/>, so controls that filter or complete on the text of an item (the path box's
    /// AutoCompleteBox) work on the path rather than on the type name.
    /// </summary>
    public override string ToString() => Path;
}
