using System.Text.Json;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Service for managing user settings and persistence.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private UserSettings _settings = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserSettings Settings => _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SharpCommander"
        );
        
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
    }

    public async Task LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.UserSettings) ?? new UserSettings();
            }
            else
            {
                _settings = new UserSettings();
                // Add default system favorites
                _settings.Favorites.AddRange(GetSystemFavorites());
            }
        }
        catch
        {
            _settings = new UserSettings();
            _settings.Favorites.AddRange(GetSystemFavorites());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_settings, AppJsonContext.Default.UserSettings);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddFavoriteAsync(string path, string? name = null)
    {
        if (string.IsNullOrEmpty(path) || IsFavorite(path))
        {
            return;
        }

        var displayName = name ?? Path.GetFileName(path);
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = path;
        }

        var favorite = new FavoriteItem
        {
            Name = displayName,
            Path = path,
            Order = _settings.Favorites.Count,
            IsSystem = false,
            CreatedAt = DateTime.Now
        };

        _settings.Favorites.Add(favorite);
        await SaveAsync();
    }

    public async Task RemoveFavoriteAsync(string path)
    {
        var favorite = _settings.Favorites.FirstOrDefault(f => 
            f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        
        if (favorite is not null && !favorite.IsSystem)
        {
            _settings.Favorites.Remove(favorite);
            await SaveAsync();
        }
    }

    public bool IsFavorite(string path)
    {
        return _settings.Favorites.Any(f => 
            f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddToHistoryAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var existing = _settings.NavigationHistory.FirstOrDefault(h => 
            h.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LastVisited = DateTime.Now;
            existing.VisitCount++;
        }
        else
        {
            var displayName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = path;
            }

            _settings.NavigationHistory.Add(new NavigationHistoryItem
            {
                Path = path,
                DisplayName = displayName,
                LastVisited = DateTime.Now,
                VisitCount = 1
            });
        }

        // Trim history if it exceeds max
        if (_settings.NavigationHistory.Count > _settings.MaxHistoryItems)
        {
            var toRemove = _settings.NavigationHistory
                .OrderBy(h => h.LastVisited)
                .Take(_settings.NavigationHistory.Count - _settings.MaxHistoryItems)
                .ToList();

            foreach (var item in toRemove)
            {
                _settings.NavigationHistory.Remove(item);
            }
        }

        await SaveAsync();
    }

    public IReadOnlyList<NavigationHistoryItem> GetRecentHistory(int count = 20)
    {
        return _settings.NavigationHistory
            .OrderByDescending(h => h.LastVisited)
            .Take(count)
            .ToList();
    }

    public async Task ClearHistoryAsync()
    {
        _settings.NavigationHistory.Clear();
        await SaveAsync();
    }

    public IReadOnlyList<FavoriteItem> GetSystemFavorites()
    {
        var favorites = new List<FavoriteItem>();
        var order = 0;

        // Desktop
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Desktop",
                Path = desktopPath,
                IconKey = "Desktop",
                Order = order++,
                IsSystem = true
            });
        }

        // Documents
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documentsPath) && Directory.Exists(documentsPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Documents",
                Path = documentsPath,
                IconKey = "Documents",
                Order = order++,
                IsSystem = true
            });
        }

        // Downloads
        var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Downloads",
                Path = downloadsPath,
                IconKey = "Downloads",
                Order = order++,
                IsSystem = true
            });
        }

        // Pictures
        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrEmpty(picturesPath) && Directory.Exists(picturesPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Pictures",
                Path = picturesPath,
                IconKey = "Pictures",
                Order = order++,
                IsSystem = true
            });
        }

        // Music
        var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrEmpty(musicPath) && Directory.Exists(musicPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Music",
                Path = musicPath,
                IconKey = "Music",
                Order = order++,
                IsSystem = true
            });
        }

        // Videos
        var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!string.IsNullOrEmpty(videosPath) && Directory.Exists(videosPath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Videos",
                Path = videosPath,
                IconKey = "Videos",
                Order = order++,
                IsSystem = true
            });
        }

        // Home folder
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(homePath) && Directory.Exists(homePath))
        {
            favorites.Add(new FavoriteItem
            {
                Name = "Home",
                Path = homePath,
                IconKey = "Home",
                Order = order++,
                IsSystem = true
            });
        }

        return favorites;
    }
}
