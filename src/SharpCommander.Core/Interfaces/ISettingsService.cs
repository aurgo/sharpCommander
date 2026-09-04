using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Persists the user settings. Mutating members schedule a debounced save through <see cref="RequestSave"/>;
/// <see cref="SaveAsync"/> writes immediately and <see cref="FlushAsync"/> writes anything still pending.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current user settings.
    /// </summary>
    UserSettings Settings { get; }

    /// <summary>
    /// Loads settings from storage. An unreadable file is kept as a backup and defaults are used.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves the settings now, atomically, superseding any pending debounced save.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Schedules a save; bursts of requests are coalesced into one write shortly after the last request.
    /// </summary>
    void RequestSave();

    /// <summary>
    /// Writes a pending save immediately and waits for any write in progress. Call it before exiting.
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// Adds a favorite directory.
    /// </summary>
    Task AddFavoriteAsync(string path, string? name = null);

    /// <summary>
    /// Removes a favorite directory, system favorites included.
    /// </summary>
    Task RemoveFavoriteAsync(string path);

    /// <summary>
    /// Changes the display name of a favorite.
    /// </summary>
    Task RenameFavoriteAsync(string path, string newName);

    /// <summary>
    /// Puts the system favorites (Desktop, Documents, ...) back at the top of the list, keeping user favorites.
    /// </summary>
    Task RestoreDefaultFavoritesAsync();

    /// <summary>
    /// Checks if a path is in favorites.
    /// </summary>
    bool IsFavorite(string path);

    /// <summary>
    /// Adds a path to navigation history (debounced save).
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
