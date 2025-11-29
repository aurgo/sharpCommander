namespace SharpCommander.Core.Models;

/// <summary>
/// Represents a favorite/bookmarked directory.
/// </summary>
public sealed class FavoriteItem
{
    /// <summary>
    /// Gets or sets the display name of the favorite.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full path of the favorite directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the icon name/key for the favorite.
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// Gets or sets the order for sorting favorites.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets whether this is a system favorite (like Desktop, Documents).
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Gets or sets the date when this favorite was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
