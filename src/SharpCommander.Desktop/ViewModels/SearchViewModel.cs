using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Advanced search: matches names with a glob (* and ?) or a regular expression compiled once, optionally
/// filters by file content off the UI thread, reports results in batches and can be cancelled at any time.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int MaxDepth = 64;
    private const long MaxContentFileSize = 10L * 1024 * 1024;
    private const int BinaryProbeLength = 8 * 1024;
    private const int BatchSize = 50;
    private static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchPath = string.Empty;

    [ObservableProperty]
    private string _fileNamePattern = "*";

    [ObservableProperty]
    private string _contentPattern = string.Empty;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateResultCommand))]
    private FileSystemEntry? _selectedResult;

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>Gets the matches found so far. Updated on the UI thread.</summary>
    public ObservableCollection<FileSystemEntry> Results { get; } = [];

    /// <summary>Gets the result the user activated (double-click or Enter), or null.</summary>
    public FileSystemEntry? ActivatedResult { get; private set; }

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    public SearchViewModel(IFileSystemService fileSystemService, string initialPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
        SearchPath = initialPath;
    }

    /// <summary>
    /// Converts a glob such as "*.txt" or "report_??.pdf" to an anchored regular expression pattern.
    /// </summary>
    public static string GlobToRegex(string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);

        var builder = new StringBuilder(glob.Length + 8).Append('^');
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        return builder.Append('$').ToString();
    }

    /// <summary>
    /// Builds the case-insensitive name matcher for a glob or a regular expression. Returns null and the
    /// parser message through <paramref name="error"/> when the expression is invalid.
    /// </summary>
    public static Regex? TryCreateNameMatcher(string pattern, bool useRegex, out string? error)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var expression = useRegex ? pattern : GlobToRegex(pattern);
        try
        {
            error = null;
            return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Stops the running search, if any. Safe to call at any time (also from the window's Closing).</summary>
    public void Cancel()
    {
        _searchCts?.Cancel();
    }

    private bool CanStartSearch() => !IsSearching;

    [RelayCommand(CanExecute = nameof(CanStartSearch))]
    private async Task StartSearchAsync()
    {
        var root = SearchPath?.Trim() ?? string.Empty;
        if (root.Length == 0 || !_fileSystemService.IsDirectory(root))
        {
            StatusText = "The search folder does not exist.";
            return;
        }

        var pattern = string.IsNullOrWhiteSpace(FileNamePattern) ? "*" : FileNamePattern.Trim();
        var matcher = TryCreateNameMatcher(pattern, UseRegex, out var error);
        if (matcher is null)
        {
            StatusText = $"Invalid pattern: {error}";
            return;
        }

        var content = ContentPattern ?? string.Empty;
        Results.Clear();
        ActivatedResult = null;
        IsSearching = true;
        StatusText = "Searching...";

        var stopwatch = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource();
        _searchCts = cts;

        try
        {
            await Task.Run(() => SearchAsync(root, matcher, content, cts.Token), cts.Token);
            StatusText = cts.IsCancellationRequested
                ? FormatOutcome("Search cancelled", stopwatch.Elapsed)
                : FormatOutcome("Search completed", stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatOutcome("Search cancelled", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            _searchCts = null;
            IsSearching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsSearching))]
    private void CancelSearch()
    {
        Cancel();
    }

    private bool CanActivateResult() => SelectedResult is not null;

    [RelayCommand(CanExecute = nameof(CanActivateResult))]
    private void ActivateResult()
    {
        if (SelectedResult is null)
        {
            return;
        }

        ActivatedResult = SelectedResult;
        Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string FormatOutcome(string outcome, TimeSpan elapsed)
    {
        var count = Results.Count;
        var items = count == 1 ? "1 item" : $"{count:N0} items";
        return $"{outcome}. Found {items} in {elapsed.TotalSeconds:0.0} s.";
    }

    /// <summary>
    /// Walks the tree breadth-first on a background thread. Symbolic links to directories are not followed
    /// so a link loop cannot make the search run forever.
    /// </summary>
    private async Task SearchAsync(string root, Regex matcher, string content, CancellationToken token)
    {
        var batch = new List<FileSystemEntry>();
        var lastFlush = Stopwatch.StartNew();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Dequeue();

            IReadOnlyList<FileSystemEntry> entries;
            try
            {
                entries = await _fileSystemService.GetEntriesAsync(directory, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Unreadable or vanished directory: skip it.
                continue;
            }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();

                if (entry.EntryType == FileSystemEntryType.ParentDirectory)
                {
                    continue;
                }

                if (entry.EntryType == FileSystemEntryType.Directory && !entry.IsSymbolicLink && depth < MaxDepth)
                {
                    pending.Enqueue((entry.FullPath, depth + 1));
                }

                if (!IsNameMatch(matcher, entry.Name))
                {
                    continue;
                }

                if (content.Length > 0 && (entry.EntryType != FileSystemEntryType.File || !ContainsText(entry, content, token)))
                {
                    continue;
                }

                batch.Add(entry);
                if (batch.Count >= BatchSize || lastFlush.Elapsed >= BatchInterval)
                {
                    await FlushAsync(batch).ConfigureAwait(false);
                    lastFlush.Restart();
                }
            }
        }

        await FlushAsync(batch).ConfigureAwait(false);
    }

    private static bool IsNameMatch(Regex matcher, string name)
    {
        try
        {
            return matcher.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private async Task FlushAsync(List<FileSystemEntry> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var items = batch.ToArray();
        batch.Clear();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var item in items)
            {
                Results.Add(item);
            }
        });
    }

    /// <summary>
    /// Case-insensitive content match. Files above 10 MiB and files that look binary (a NUL byte in the
    /// first 8 KiB) are skipped.
    /// </summary>
    private static bool ContainsText(FileSystemEntry entry, string text, CancellationToken token)
    {
        if (entry.Size > MaxContentFileSize)
        {
            return false;
        }

        byte[]? buffer = null;
        try
        {
            using var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
            var length = (int)Math.Min(stream.Length, MaxContentFileSize);
            buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));

            var read = 0;
            while (read < length)
            {
                token.ThrowIfCancellationRequested();
                var count = stream.Read(buffer, read, length - read);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (LooksBinary(buffer, read))
            {
                return false;
            }

            var decoded = Encoding.UTF8.GetString(buffer, 0, read);
            return decoded.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static bool LooksBinary(byte[] buffer, int length)
    {
        return Array.IndexOf(buffer, (byte)0, 0, Math.Min(length, BinaryProbeLength)) >= 0;
    }
}
