using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Interface for managing user settings persistence.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current user settings.
    /// </summary>
    UserSettings Settings { get; }

    /// <summary>
    /// Loads settings from storage.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves settings to storage.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Adds a favorite directory.
    /// </summary>
    Task AddFavoriteAsync(string path, string? name = null);

    /// <summary>
    /// Removes a favorite directory.
    /// </summary>
    Task RemoveFavoriteAsync(string path);

    /// <summary>
    /// Checks if a path is in favorites.
    /// </summary>
    bool IsFavorite(string path);

    /// <summary>
    /// Adds a path to navigation history.
    /// </summary>
    Task AddToHistoryAsync(string path);

    /// <summary>
    /// Gets the navigation history sorted by most recent.
    /// </summary>
    IReadOnlyList<NavigationHistoryItem> GetRecentHistory(int count = 20);

    /// <summary>
    /// Clears the navigation history.
    /// </summary>
    Task ClearHistoryAsync();

    /// <summary>
    /// Gets system favorites (Desktop, Documents, Downloads, etc.).
    /// </summary>
    IReadOnlyList<FavoriteItem> GetSystemFavorites();
}
