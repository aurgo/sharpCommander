using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>In-memory settings so tests never touch the user's real settings.json.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    public UserSettings Settings { get; } = new();
    public int SaveCount { get; private set; }
    public int RequestSaveCount { get; private set; }
    public int FlushCount { get; private set; }

    public Task LoadAsync() => Task.CompletedTask;
    public Task SaveAsync() { SaveCount++; return Task.CompletedTask; }
    public void RequestSave() => RequestSaveCount++;
    public Task FlushAsync() { FlushCount++; return Task.CompletedTask; }

    public Task AddFavoriteAsync(string path, string? name = null)
    {
        if (!IsFavorite(path))
        {
            Settings.Favorites.Add(new FavoriteItem { Path = path, Name = name ?? System.IO.Path.GetFileName(path), Order = Settings.Favorites.Count });
        }
        return Task.CompletedTask;
    }

    public Task RemoveFavoriteAsync(string path)
    {
        Settings.Favorites.RemoveAll(f => f.Path == path);
        return Task.CompletedTask;
    }

    public Task RenameFavoriteAsync(string path, string newName)
    {
        foreach (var favorite in Settings.Favorites.Where(f => f.Path == path))
        {
            favorite.Name = newName;
        }
        return Task.CompletedTask;
    }

    public Task RestoreDefaultFavoritesAsync()
    {
        var user = Settings.Favorites.Where(f => !f.IsSystem).ToList();
        Settings.Favorites.Clear();
        Settings.Favorites.AddRange(GetSystemFavorites());
        Settings.Favorites.AddRange(user);
        return Task.CompletedTask;
    }

    public bool IsFavorite(string path) => Settings.Favorites.Any(f => f.Path == path);

    public Task AddToHistoryAsync(string path)
    {
        Settings.NavigationHistory.Add(new NavigationHistoryItem { Path = path, DisplayName = path });
        return Task.CompletedTask;
    }

    public IReadOnlyList<NavigationHistoryItem> GetRecentHistory(int count = 20) => Settings.NavigationHistory.TakeLast(count).ToList();
    public Task ClearHistoryAsync() { Settings.NavigationHistory.Clear(); return Task.CompletedTask; }
    public IReadOnlyList<FavoriteItem> GetSystemFavorites() => [];
}
