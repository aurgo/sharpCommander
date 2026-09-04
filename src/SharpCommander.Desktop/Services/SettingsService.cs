using System.Text.Json;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Persists <see cref="UserSettings"/> as JSON in <see cref="AppPaths.SettingsFile"/>. Every write is atomic
/// (settings.json.tmp is written and then renamed over the file), <see cref="RequestSave"/> coalesces bursts of
/// changes into one write half a second after the last request, <see cref="FlushAsync"/> writes anything still
/// pending, and a file that cannot be parsed is kept as settings.json.bak instead of being overwritten with
/// defaults. Snapshots are serialized when a save is requested, so later mutations never race the writer, and a
/// sequence number guarantees an older snapshot can never overwrite a newer one.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _settingsFile;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly object _pendingGate = new();
    private CancellationTokenSource? _pendingTimer;
    private (string Json, long Sequence)? _pendingSnapshot;
    private Task? _lastWrite;
    private long _snapshotSequence;
    private long _writtenSequence;
    private UserSettings _settings = new();

    public UserSettings Settings => _settings;

    /// <summary>Gets the file the settings are stored in.</summary>
    public string SettingsFile => _settingsFile;

    public SettingsService()
        : this(AppPaths.SettingsFile)
    {
    }

    /// <summary>Creates a service that stores its settings in <paramref name="settingsFile"/> (used by tests).</summary>
    internal SettingsService(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = settingsFile;
    }

    public async Task LoadAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _settings = await Task.Run(ReadSettingsFile).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public Task SaveAsync()
    {
        var snapshot = TakeSnapshot();
        DiscardPendingSave();
        return WriteAsync(snapshot);
    }

    public void RequestSave()
    {
        var snapshot = TakeSnapshot();
        CancellationTokenSource timer;

        lock (_pendingGate)
        {
            _pendingTimer?.Cancel();
            _pendingTimer?.Dispose();
            timer = new CancellationTokenSource();
            _pendingTimer = timer;
            _pendingSnapshot = snapshot;
        }

        _ = SaveLaterAsync(timer);
    }

    public async Task FlushAsync()
    {
        var pending = DiscardPendingSave();

        Task? inFlight;
        lock (_pendingGate)
        {
            inFlight = _lastWrite;
        }

        if (inFlight is not null)
        {
            try
            {
                await inFlight.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Already reported by the caller that started that write.
            }
        }

        if (pending is { } snapshot)
        {
            await WriteAsync(snapshot).ConfigureAwait(false);
        }
    }

    // ---- favorites --------------------------------------------------------------------------------------

    public Task AddFavoriteAsync(string path, string? name = null)
    {
        if (string.IsNullOrEmpty(path) || IsFavorite(path))
        {
            return Task.CompletedTask;
        }

        var displayName = string.IsNullOrWhiteSpace(name) ? DisplayNameFor(path) : name.Trim();

        _settings.Favorites.Add(new FavoriteItem
        {
            Name = displayName,
            Path = path,
            Order = _settings.Favorites.Count,
            IsSystem = false,
            CreatedAt = DateTime.Now
        });

        RequestSave();
        return Task.CompletedTask;
    }

    public Task RemoveFavoriteAsync(string path)
    {
        var favorite = FindFavorite(path);
        if (favorite is null)
        {
            return Task.CompletedTask;
        }

        _settings.Favorites.Remove(favorite);
        RenumberFavorites();
        RequestSave();
        return Task.CompletedTask;
    }

    public Task RenameFavoriteAsync(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var favorite = FindFavorite(path);
        if (favorite is null || string.Equals(favorite.Name, newName.Trim(), StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        favorite.Name = newName.Trim();
        RequestSave();
        return Task.CompletedTask;
    }

    public Task RestoreDefaultFavoritesAsync()
    {
        var system = GetSystemFavorites();
        var user = _settings.Favorites
            .Where(f => !f.IsSystem && !system.Any(s => SamePath(s.Path, f.Path)))
            .OrderBy(f => f.Order)
            .ToList();

        _settings.Favorites.Clear();
        _settings.Favorites.AddRange(system);
        _settings.Favorites.AddRange(user);
        RenumberFavorites();
        RequestSave();
        return Task.CompletedTask;
    }

    public bool IsFavorite(string path)
    {
        return FindFavorite(path) is not null;
    }

    // ---- history ----------------------------------------------------------------------------------------

    public Task AddToHistoryAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.CompletedTask;
        }

        var existing = _settings.NavigationHistory.FirstOrDefault(h => SamePath(h.Path, path));
        if (existing is not null)
        {
            existing.LastVisited = DateTime.Now;
            existing.VisitCount++;
        }
        else
        {
            _settings.NavigationHistory.Add(new NavigationHistoryItem
            {
                Path = path,
                DisplayName = DisplayNameFor(path),
                LastVisited = DateTime.Now,
                VisitCount = 1
            });
        }

        TrimHistory();
        RequestSave();
        return Task.CompletedTask;
    }

    public IReadOnlyList<NavigationHistoryItem> GetRecentHistory(int count = 20)
    {
        return _settings.NavigationHistory
            .OrderByDescending(h => h.LastVisited)
            .Take(count)
            .ToList();
    }

    public Task ClearHistoryAsync()
    {
        _settings.NavigationHistory.Clear();
        RequestSave();
        return Task.CompletedTask;
    }

    public IReadOnlyList<FavoriteItem> GetSystemFavorites()
    {
        var favorites = new List<FavoriteItem>();

        void Add(string name, string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                favorites.Add(new FavoriteItem
                {
                    Name = name,
                    Path = path,
                    IconKey = name,
                    Order = favorites.Count,
                    IsSystem = true
                });
            }
        }

        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Downloads", GetDownloadsDirectory());
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return favorites;
    }

    /// <summary>
    /// The user's downloads folder. Every other system favorite has an <see cref="Environment.SpecialFolder"/>,
    /// but downloads does not, so on Linux the XDG user-dirs configuration is read: a localized setup points
    /// XDG_DOWNLOAD_DIR at "Descargas", "Téléchargements" or "Downloads" depending on the install language, and
    /// assuming the English name silently dropped the favorite.
    /// </summary>
    private static string GetDownloadsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsLinux() && ReadXdgUserDirectory("XDG_DOWNLOAD_DIR", home) is { } configured)
        {
            return configured;
        }

        return Path.Combine(home, "Downloads");
    }

    /// <summary>
    /// Reads one entry from the freedesktop user-dirs file, which holds lines of the form
    /// XDG_DOWNLOAD_DIR="$HOME/Descargas". Returns null when the file, the entry or the folder is missing.
    /// </summary>
    private static string? ReadXdgUserDirectory(string key, string home)
    {
        try
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configDirectory = string.IsNullOrWhiteSpace(configHome) ? Path.Combine(home, ".config") : configHome;
            var file = Path.Combine(configDirectory, "user-dirs.dirs");

            if (!File.Exists(file))
            {
                return null;
            }

            foreach (var line in File.ReadLines(file))
            {
                var text = line.AsSpan().Trim();
                if (text.IsEmpty || text[0] == '#' || !text.StartsWith(key, StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = text.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                var value = text[(separator + 1)..].Trim().Trim('"').ToString();
                if (value.StartsWith("$HOME", StringComparison.Ordinal))
                {
                    value = home + value["$HOME".Length..];
                }

                return value.Length > 0 && Directory.Exists(value) ? value : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable user-dirs file just means the default is used.
        }

        return null;
    }

    // ---- reading ----------------------------------------------------------------------------------------

    private UserSettings ReadSettingsFile()
    {
        if (!File.Exists(_settingsFile))
        {
            return CreateDefaults();
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.UserSettings)
                ?? throw new JsonException("The settings file does not contain a settings object.");
        }
        catch (JsonException ex)
        {
            BackupCorruptFile(ex);
            return CreateDefaults();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The settings file '{_settingsFile}' could not be read; default settings are used.", ex);
            return CreateDefaults();
        }
    }

    private void BackupCorruptFile(Exception error)
    {
        var backup = _settingsFile + ".bak";
        try
        {
            File.Move(_settingsFile, backup, overwrite: true);
            AppLog.Warning($"The settings file could not be parsed and was moved to '{backup}'; default settings are used.", error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The settings file '{_settingsFile}' could not be parsed nor backed up.", ex);
        }
    }

    private UserSettings CreateDefaults()
    {
        var settings = new UserSettings();
        settings.Favorites.AddRange(GetSystemFavorites());
        return settings;
    }

    // ---- writing ----------------------------------------------------------------------------------------

    private (string Json, long Sequence) TakeSnapshot()
    {
        var json = JsonSerializer.Serialize(_settings, AppJsonContext.Default.UserSettings);
        return (json, Interlocked.Increment(ref _snapshotSequence));
    }

    /// <summary>Cancels the debounce timer and returns the snapshot it was going to write, if any.</summary>
    private (string Json, long Sequence)? DiscardPendingSave()
    {
        lock (_pendingGate)
        {
            _pendingTimer?.Cancel();
            _pendingTimer?.Dispose();
            _pendingTimer = null;

            var pending = _pendingSnapshot;
            _pendingSnapshot = null;
            return pending;
        }
    }

    private async Task SaveLaterAsync(CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(SaveDelay, timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, an explicit save or a flush.
            return;
        }

        (string Json, long Sequence)? snapshot;
        lock (_pendingGate)
        {
            if (!ReferenceEquals(_pendingTimer, timer))
            {
                return;
            }

            _pendingTimer = null;
            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
        }

        timer.Dispose();
        if (snapshot is null)
        {
            return;
        }

        try
        {
            await WriteAsync(snapshot.Value).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"The settings could not be saved to '{_settingsFile}'.", ex);
        }
    }

    private Task WriteAsync((string Json, long Sequence) snapshot)
    {
        lock (_pendingGate)
        {
            _lastWrite = WriteCoreAsync(snapshot.Json, snapshot.Sequence);
            return _lastWrite;
        }
    }

    private async Task WriteCoreAsync(string json, long sequence)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (sequence <= _writtenSequence)
            {
                // A newer snapshot already reached the disk.
                return;
            }

            await Task.Run(() => WriteAtomically(_settingsFile, json)).ConfigureAwait(false);
            _writtenSequence = sequence;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private FavoriteItem? FindFavorite(string path)
    {
        return string.IsNullOrEmpty(path) ? null : _settings.Favorites.FirstOrDefault(f => SamePath(f.Path, path));
    }

    private void RenumberFavorites()
    {
        for (var i = 0; i < _settings.Favorites.Count; i++)
        {
            _settings.Favorites[i].Order = i;
        }
    }

    private void TrimHistory()
    {
        var excess = _settings.NavigationHistory.Count - _settings.MaxHistoryItems;
        if (excess <= 0)
        {
            return;
        }

        foreach (var item in _settings.NavigationHistory.OrderBy(h => h.LastVisited).Take(excess).ToList())
        {
            _settings.NavigationHistory.Remove(item);
        }
    }

    private static bool SamePath(string a, string b)
    {
        return string.Equals(a, b, PathUtils.PathComparison);
    }

    private static string DisplayNameFor(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
