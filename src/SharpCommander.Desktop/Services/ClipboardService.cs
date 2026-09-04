using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// The file clipboard used by copy, cut and paste. Implementations put the files on the operating system
/// clipboard when one is available so other applications can paste them, read files placed there by other
/// applications, and keep an internal list as fallback.
/// </summary>
public interface IClipboardService
{
    /// <summary>Places the items on the clipboard for a later copy.</summary>
    Task CopyAsync(IEnumerable<FileSystemEntry> items);

    /// <summary>Places the items on the clipboard for a later move; <see cref="IsCutMode"/> becomes true.</summary>
    Task CutAsync(IEnumerable<FileSystemEntry> items);

    /// <summary>
    /// Returns the paths currently on the clipboard: the files any application placed there, or the internal list
    /// when the system clipboard is unavailable. Also refreshes <see cref="IsCutMode"/>, which is reset when the
    /// clipboard holds content this service did not write.
    /// </summary>
    Task<IReadOnlyList<string>> GetPathsAsync();

    /// <summary>True when the paths returned by the last <see cref="GetPathsAsync"/> are to be moved rather than copied.</summary>
    bool IsCutMode { get; }

    /// <summary>Forgets the internal list and clears the system clipboard when it still holds this service's content.</summary>
    Task ClearAsync();
}

/// <summary>
/// Backs <see cref="IClipboardService"/> with the Avalonia clipboard (DataTransfer API). Files are written as
/// storage items plus a text fallback listing the paths and a private marker; on reading, the marker (or the
/// in-process data object) tells our own content apart from files copied in Finder or Explorer, which are
/// always pasted as copies. Without a top level window (headless tests) only the internal list is used.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private static readonly DataFormat<string> MarkerFormat = DataFormat.CreateStringApplicationFormat("SharpCommander.FileList");

    private readonly Func<TopLevel?> _topLevelProvider;
    private IReadOnlyList<string> _paths = [];
    private bool _isCutMode;
    private string? _token;
    private string? _text;
    private DataTransfer? _transfer;
    private bool _onSystemClipboard;

    public ClipboardService()
        : this(FindMainTopLevel)
    {
    }

    /// <summary>Creates a service reading the clipboard of the top level returned by <paramref name="topLevelProvider"/>.</summary>
    internal ClipboardService(Func<TopLevel?> topLevelProvider)
    {
        ArgumentNullException.ThrowIfNull(topLevelProvider);
        _topLevelProvider = topLevelProvider;
    }

    public bool IsCutMode => _isCutMode;

    public Task CopyAsync(IEnumerable<FileSystemEntry> items) => SetAsync(items, cut: false);

    public Task CutAsync(IEnumerable<FileSystemEntry> items) => SetAsync(items, cut: true);

    public Task<IReadOnlyList<string>> GetPathsAsync() => OnUiThreadAsync(GetPathsCoreAsync);

    public Task ClearAsync() => OnUiThreadAsync(ClearCoreAsync);

    private Task SetAsync(IEnumerable<FileSystemEntry> items, bool cut)
    {
        ArgumentNullException.ThrowIfNull(items);

        var paths = items
            .Where(item => item.EntryType is FileSystemEntryType.File or FileSystemEntryType.Directory)
            .Select(item => item.FullPath)
            .Distinct(PathUtils.PathComparer)
            .ToList();

        return OnUiThreadAsync(() => SetCoreAsync(paths, cut));
    }

    private async Task SetCoreAsync(IReadOnlyList<string> paths, bool cut)
    {
        _paths = paths;
        _isCutMode = cut && paths.Count > 0;
        _token = Guid.NewGuid().ToString("N");
        _text = string.Join(Environment.NewLine, paths);
        _transfer = null;
        _onSystemClipboard = false;

        var topLevel = _topLevelProvider();
        if (topLevel?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            if (paths.Count == 0)
            {
                await clipboard.ClearAsync();
                return;
            }

            var transfer = await BuildTransferAsync(topLevel.StorageProvider, paths, _text, _token);
            await clipboard.SetDataAsync(transfer);
            _transfer = transfer;
            _onSystemClipboard = true;
        }
        catch (Exception ex)
        {
            AppLog.Warning("The files could not be placed on the system clipboard; they are kept in the internal list.", ex);
        }
    }

    private static async Task<DataTransfer> BuildTransferAsync(IStorageProvider storage, IReadOnlyList<string> paths, string text, string token)
    {
        var transfer = new DataTransfer();

        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(path)
                : await storage.TryGetFileFromPathAsync(path);

            if (item is not null)
            {
                transfer.Add(DataTransferItem.CreateFile(item));
            }
        }

        var fallback = DataTransferItem.CreateText(text);
        fallback.Set(MarkerFormat, token);
        transfer.Add(fallback);
        return transfer;
    }

    private async Task<IReadOnlyList<string>> GetPathsCoreAsync()
    {
        if (_topLevelProvider()?.Clipboard is not { } clipboard)
        {
            return _paths;
        }

        try
        {
            var content = await InspectAsync(clipboard);
            switch (content.Kind)
            {
                case ContentKind.Ours:
                    return _paths;

                case ContentKind.External:
                    AdoptExternal(content.Paths);
                    return _paths;

                default:
                    // Empty or foreign content: our files are gone from the clipboard, unless they never got there.
                    if (_onSystemClipboard)
                    {
                        Forget();
                    }

                    return _paths;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("The system clipboard could not be read; the internal file list is used.", ex);
            return _paths;
        }
    }

    private async Task ClearCoreAsync()
    {
        var wasOnSystemClipboard = _onSystemClipboard;
        var clipboard = _topLevelProvider()?.Clipboard;
        Task<ClipboardContent>? inspection = wasOnSystemClipboard && clipboard is not null ? InspectAsync(clipboard) : null;

        try
        {
            if (inspection is not null && (await inspection).Kind == ContentKind.Ours)
            {
                await clipboard!.ClearAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("The system clipboard could not be cleared.", ex);
        }
        finally
        {
            Forget();
        }
    }

    /// <summary>Classifies what the system clipboard holds relative to the last content this service wrote.</summary>
    private async Task<ClipboardContent> InspectAsync(IClipboard clipboard)
    {
        if (_transfer is not null)
        {
            var inProcess = await clipboard.TryGetInProcessDataAsync();
            if (ReferenceEquals(inProcess, _transfer))
            {
                return ClipboardContent.Ours;
            }
        }

        using var data = await clipboard.TryGetDataAsync();
        if (data is null)
        {
            return ClipboardContent.Empty;
        }

        if (_token is not null && string.Equals(await data.TryGetValueAsync(MarkerFormat), _token, StringComparison.Ordinal))
        {
            return ClipboardContent.Ours;
        }

        if (_text is not null && string.Equals(await data.TryGetTextAsync(), _text, StringComparison.Ordinal))
        {
            return ClipboardContent.Ours;
        }

        var files = await data.TryGetFilesAsync();
        if (files is { Length: > 0 })
        {
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Cast<string>()
                .ToList();

            if (paths.Count > 0)
            {
                return new ClipboardContent(ContentKind.External, paths);
            }
        }

        return ClipboardContent.Foreign;
    }

    private void AdoptExternal(IReadOnlyList<string> paths)
    {
        _paths = paths;
        _isCutMode = false;
        _token = null;
        _text = null;
        _transfer = null;
        _onSystemClipboard = false;
    }

    private void Forget()
    {
        AdoptExternal([]);
    }

    private static TopLevel? FindMainTopLevel()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    private static Task OnUiThreadAsync(Func<Task> action)
    {
        return Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
    }

    private static Task<T> OnUiThreadAsync<T>(Func<Task<T>> action)
    {
        return Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
    }

    private enum ContentKind
    {
        Empty,
        Ours,
        External,
        Foreign
    }

    private sealed record ClipboardContent(ContentKind Kind, IReadOnlyList<string> Paths)
    {
        public static ClipboardContent Empty { get; } = new(ContentKind.Empty, []);
        public static ClipboardContent Ours { get; } = new(ContentKind.Ours, []);
        public static ClipboardContent Foreign { get; } = new(ContentKind.Foreign, []);
    }
}
