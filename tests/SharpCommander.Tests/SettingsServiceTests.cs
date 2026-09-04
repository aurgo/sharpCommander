using System.Text.Json;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>Settings persistence against a temp file: atomic writes, backup of corrupt files, debounce and flush.</summary>
public class SettingsServiceTests
{
    private static readonly TimeSpan DebounceMargin = TimeSpan.FromMilliseconds(900);

    private static string SettingsFile(TempDir dir) => Path.Combine(dir.Path, "config", "settings.json");

    [Fact]
    public async Task SaveAsync_WritesValidJsonWithoutLeavingTempFiles()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        var service = new SettingsService(file);
        service.Settings.Theme = "Dark";
        service.Settings.FavoritesPanelVisible = false;
        service.Settings.WindowWidth = 1024;

        await service.SaveAsync();

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("Dark", document.RootElement.GetProperty("theme").GetString());

        var reloaded = new SettingsService(file);
        await reloaded.LoadAsync();
        Assert.Equal("Dark", reloaded.Settings.Theme);
        Assert.False(reloaded.Settings.FavoritesPanelVisible);
        Assert.Equal(1024, reloaded.Settings.WindowWidth);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_IsBackedUpAndDefaultsUsed()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ this is not json");
        var service = new SettingsService(file);

        await service.LoadAsync();

        Assert.True(File.Exists(file + ".bak"));
        Assert.Equal("{ this is not json", File.ReadAllText(file + ".bak"));
        Assert.False(File.Exists(file));
        Assert.Equal("System", service.Settings.Theme);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_UsesDefaultsWithSystemFavorites()
    {
        using var dir = new TempDir();
        var service = new SettingsService(SettingsFile(dir));

        await service.LoadAsync();

        Assert.Equal(service.GetSystemFavorites().Count, service.Settings.Favorites.Count);
        Assert.All(service.Settings.Favorites, favorite => Assert.True(favorite.IsSystem));
    }

    [Fact]
    public async Task RequestSave_IsDebouncedAndWritesTheLastState()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        var service = new SettingsService(file);

        service.Settings.Theme = "Light";
        service.RequestSave();
        service.Settings.Theme = "Dark";
        service.RequestSave();

        Assert.False(File.Exists(file), "the write is delayed");

        await Task.Delay(DebounceMargin);

        Assert.True(File.Exists(file));
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("Dark", document.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task FlushAsync_WritesAPendingSaveImmediately()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        var service = new SettingsService(file);
        service.Settings.Theme = "Light";
        service.RequestSave();

        await service.FlushAsync();

        Assert.True(File.Exists(file));
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("Light", document.RootElement.GetProperty("theme").GetString());

        // A flush with nothing pending is a no-op.
        File.Delete(file);
        await service.FlushAsync();
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task SaveAsync_SupersedesAPendingDebouncedSave()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        var service = new SettingsService(file);
        service.Settings.Theme = "Light";
        service.RequestSave();
        service.Settings.Theme = "Dark";

        await service.SaveAsync();
        await Task.Delay(DebounceMargin);

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("Dark", document.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task AddToHistory_DoesNotWriteImmediately()
    {
        using var dir = new TempDir();
        var file = SettingsFile(dir);
        var service = new SettingsService(file);

        await service.AddToHistoryAsync(dir.Path);

        Assert.False(File.Exists(file));
        Assert.Single(service.Settings.NavigationHistory);
        await service.FlushAsync();
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Favorites_RemoveRenameAndRestoreDefaults()
    {
        using var dir = new TempDir();
        var service = new SettingsService(SettingsFile(dir));
        await service.LoadAsync();
        var system = service.GetSystemFavorites();
        var user = dir.Dir("mine");

        await service.AddFavoriteAsync(user);
        await service.RenameFavoriteAsync(user, "My folder");
        Assert.Equal("My folder", service.Settings.Favorites.Single(f => f.Path == user).Name);

        if (system.Count > 0)
        {
            await service.RemoveFavoriteAsync(system[0].Path);
            Assert.False(service.IsFavorite(system[0].Path), "system favorites can be removed too");
        }

        await service.RestoreDefaultFavoritesAsync();

        Assert.Equal(system.Select(f => f.Path).Append(user), service.Settings.Favorites.Select(f => f.Path));
        Assert.Equal(Enumerable.Range(0, service.Settings.Favorites.Count), service.Settings.Favorites.Select(f => f.Order));
        Assert.Equal("My folder", service.Settings.Favorites.Last().Name);
        await service.FlushAsync();
    }

    [Fact]
    public void UserSettings_DefaultsCoverTheNewMembers()
    {
        var settings = new UserSettings();

        Assert.True(settings.FavoritesPanelVisible);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        Assert.Null(settings.WindowState);
        Assert.Equal("Name", settings.SortColumn);
        Assert.Equal("Ascending", settings.SortDirection);
    }
}
