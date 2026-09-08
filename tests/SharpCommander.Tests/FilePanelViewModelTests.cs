using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Sorting, hidden files, refresh versus navigation, stale loads, type-ahead and selection of the file panel,
/// with fakes for settings, dialogs, clipboard and trash and the real file system service on temp folders.
/// </summary>
public class FilePanelViewModelTests
{
    private static readonly FileSystemService RealFileSystem = new();

    private sealed class Harness
    {
        public FakeSettingsService Settings { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeClipboardService Clipboard { get; } = new();
        public FakeTrashService Trash { get; } = new();
        public FilePanelViewModel Panel { get; }

        public Harness(IFileSystemService? fileSystem = null)
        {
            var fs = fileSystem ?? RealFileSystem;
            Panel = new FilePanelViewModel(fs, Settings, Dialogs, Clipboard, new FileOperationsService(fs, Dialogs, Trash));
        }
    }

    private static IEnumerable<string> Names(FilePanelViewModel panel) => panel.FilteredEntries.Select(entry => entry.Name);

    /// <summary>Selects every entry but "..", the way Select All does from the main window.</summary>
    private static void SelectEverything(FilePanelViewModel panel)
    {
        panel.SelectedEntries.Clear();
        foreach (var entry in panel.FilteredEntries.Where(entry => entry.EntryType != FileSystemEntryType.ParentDirectory))
        {
            panel.SelectedEntries.Add(entry);
        }
    }

    private static void MakeHidden(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
    }

    // ---- H5: sorting ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Listing_HasDirectoriesFirstInNaturalOrder()
    {
        using var dir = new TempDir();
        dir.Dir("zdir");
        dir.Dir("adir");
        dir.File("file10.txt");
        dir.File("file2.txt");
        dir.File("Beta.txt");
        dir.File("alpha.txt");
        var harness = new Harness();

        await harness.Panel.InitializeAsync(dir.Path);

        Assert.Equal(new[] { "..", "adir", "zdir", "alpha.txt", "Beta.txt", "file2.txt", "file10.txt" }, Names(harness.Panel));
        Assert.Equal(FileSystemEntryType.ParentDirectory, harness.Panel.SelectedEntry?.EntryType);
    }

    [AvaloniaFact]
    public async Task SortBy_SizeAndModified_ToggleDirectionAndPersist()
    {
        using var dir = new TempDir();
        dir.Dir("folder");
        var a = dir.File("a.txt", "x");
        var b = dir.File("b.txt", "xxx");
        var c = dir.File("c.txt", "xx");
        var now = DateTime.Now;
        File.SetLastWriteTime(a, now.AddHours(-3));
        File.SetLastWriteTime(c, now.AddHours(-2));
        File.SetLastWriteTime(b, now.AddHours(-1));
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        panel.SortByCommand.Execute("Size");
        Assert.Equal(new[] { "..", "folder", "a.txt", "c.txt", "b.txt" }, Names(panel));
        Assert.Equal("Size", panel.SortColumn);
        Assert.False(panel.SortDescending);
        Assert.Equal("▲", panel.SizeSortIndicator);
        Assert.Equal(string.Empty, panel.NameSortIndicator);

        panel.SortByCommand.Execute("Size");
        Assert.Equal(new[] { "..", "folder", "b.txt", "c.txt", "a.txt" }, Names(panel));
        Assert.True(panel.SortDescending);
        Assert.Equal("Size", harness.Settings.Settings.SortColumn);
        Assert.Equal("Descending", harness.Settings.Settings.SortDirection);
        Assert.True(harness.Settings.RequestSaveCount >= 2);

        panel.SortByCommand.Execute("Modified");
        Assert.Equal(new[] { "..", "folder", "a.txt", "c.txt", "b.txt" }, Names(panel));
        Assert.False(panel.SortDescending);

        panel.ToggleSortDirectionCommand.Execute(null);
        Assert.Equal(new[] { "..", "folder", "b.txt", "c.txt", "a.txt" }, Names(panel));

        panel.SortByCommand.Execute("Name");
        Assert.Equal(new[] { "..", "folder", "a.txt", "b.txt", "c.txt" }, Names(panel));
    }

    [AvaloniaFact]
    public async Task Sort_IsLoadedFromTheSettings()
    {
        using var dir = new TempDir();
        dir.File("a.txt", "x");
        dir.File("b.txt", "xxx");
        var harness = new Harness();
        harness.Settings.Settings.SortColumn = "size";
        harness.Settings.Settings.SortDirection = "Descending";

        await harness.Panel.InitializeAsync(dir.Path);

        Assert.Equal("Size", harness.Panel.SortColumn);
        Assert.True(harness.Panel.SortDescending);
        Assert.Equal(new[] { "..", "b.txt", "a.txt" }, Names(harness.Panel));
        Assert.Equal(0, harness.Settings.RequestSaveCount);
    }

    // ---- L1: hidden files ------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task HiddenFiles_AreFilteredUntilToggled()
    {
        using var dir = new TempDir();
        dir.File("visible.txt");
        MakeHidden(dir.File(".hidden.txt"));
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        Assert.Equal(new[] { "..", "visible.txt" }, Names(panel));
        Assert.Contains(panel.Entries, entry => entry.Name == ".hidden.txt" && entry.IsHidden);

        panel.ToggleShowHiddenFilesCommand.Execute(null);

        Assert.True(panel.ShowHiddenFiles);
        Assert.Equal(new[] { "..", ".hidden.txt", "visible.txt" }, Names(panel));
        Assert.True(harness.Settings.Settings.ShowHiddenFiles);
        Assert.Equal(1, harness.Settings.RequestSaveCount);

        panel.ToggleShowHiddenFilesCommand.Execute(null);
        Assert.Equal(new[] { "..", "visible.txt" }, Names(panel));
    }

    // ---- M4 / M5: refresh is not a navigation --------------------------------------------------------------

    [AvaloniaFact]
    public async Task Refresh_PreservesSelectionInPlaceAndDoesNotTouchHistory()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.File("b.txt");
        dir.File("c.txt");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        Assert.Single(harness.Settings.Settings.NavigationHistory);
        var entryA = panel.FilteredEntries.Single(entry => entry.Name == "a.txt");
        Assert.True(panel.SelectPath(b));
        var reveals = 0;
        panel.SelectionRevealRequested += (_, _) => reveals++;

        dir.File("d.txt");
        await panel.RefreshAsync();

        Assert.Equal(new[] { "..", "a.txt", "b.txt", "c.txt", "d.txt" }, Names(panel));
        Assert.Same(entryA, panel.FilteredEntries.Single(entry => entry.Name == "a.txt"));
        Assert.Equal("b.txt", panel.SelectedEntry?.Name);
        Assert.Equal(["b.txt"], panel.SelectedEntries.Select(entry => entry.Name));
        Assert.Single(harness.Settings.Settings.NavigationHistory);
        Assert.Equal(0, reveals);
        Assert.Contains("4 files", panel.StatusText);
        Assert.Contains("1 selected", panel.StatusText);
        Assert.Equal(a, entryA.FullPath);
    }

    [AvaloniaFact]
    public async Task Refresh_ReplacesChangedEntriesAndDropsDeletedOnes()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt", "x");
        var b = dir.File("b.txt", "x");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        var before = panel.FilteredEntries.Single(entry => entry.Name == "a.txt");
        panel.SelectPath(a);
        panel.SelectedEntries.Add(panel.FilteredEntries.Single(entry => entry.Name == "b.txt"));

        File.WriteAllText(a, "xxx");
        File.SetLastWriteTime(a, DateTime.Now.AddMinutes(1));
        File.Delete(b);
        await panel.RefreshAsync();

        var after = panel.FilteredEntries.Single(entry => entry.Name == "a.txt");
        Assert.NotSame(before, after);
        Assert.Equal(3, after.Size);
        Assert.Equal(new[] { "..", "a.txt" }, Names(panel));
        Assert.Same(after, panel.SelectedEntry);
        Assert.Equal([after], panel.SelectedEntries);
    }

    [AvaloniaFact]
    public async Task Refresh_WhenTheFolderVanished_GoesToTheNearestExistingParent()
    {
        using var dir = new TempDir();
        var sub = dir.Dir(Path.Combine("a", "b"));
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(sub);

        Directory.Delete(Path.Combine(dir.Path, "a"), recursive: true);
        await panel.RefreshAsync();

        Assert.Equal(dir.Path, panel.CurrentPath);
    }

    // ---- M8: typed paths -------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NavigateTo_MissingPath_KeepsTheFolderAndReports()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        var missing = Path.Combine(dir.Path, "nope");
        panel.EditablePath = missing;

        await panel.NavigateToPathCommand.ExecuteAsync(null);

        Assert.Equal(dir.Path, panel.CurrentPath);
        Assert.Equal(dir.Path, panel.EditablePath);
        Assert.StartsWith("Path not found", panel.StatusText);
        Assert.Contains("error:Path not found", harness.Dialogs.Calls);
        Assert.Equal(new[] { "..", "a.txt" }, Names(panel));
        Assert.False(panel.IsRootView);
    }

    [AvaloniaFact]
    public async Task NavigateTo_FilePath_OpensItAndStaysInTheFolder()
    {
        using var dir = new TempDir();
        var file = dir.File("doc.txt");
        var fileSystem = new DelegatingFileSystem(RealFileSystem);
        var harness = new Harness(fileSystem);
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        await panel.NavigateToCommand.ExecuteAsync(file);

        Assert.Equal([file], fileSystem.Opened);
        Assert.Equal(dir.Path, panel.CurrentPath);
        Assert.Equal(dir.Path, panel.EditablePath);
        Assert.Equal("Opened 'doc.txt'.", panel.StatusText);
        Assert.DoesNotContain(harness.Dialogs.Calls, call => call.StartsWith("error:", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task NavigateTo_EmptyPath_ShowsTheDrives()
    {
        using var dir = new TempDir();
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        await panel.NavigateToCommand.ExecuteAsync(string.Empty);

        Assert.True(panel.IsRootView);
        Assert.Equal(string.Empty, panel.CurrentPath);
        Assert.NotEmpty(panel.FilteredEntries);
        Assert.All(panel.FilteredEntries, entry => Assert.Equal(FileSystemEntryType.Drive, entry.EntryType));
        Assert.EndsWith(panel.FilteredEntries.Count == 1 ? "drive" : "drives", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task AccessDenied_KeepsTheFolderAndReportsOnce()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        using var dir = new TempDir();
        var locked = dir.Dir("locked");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            await panel.NavigateToCommand.ExecuteAsync(locked);

            Assert.Equal(dir.Path, panel.CurrentPath);
            Assert.StartsWith("Access denied", panel.StatusText);
            Assert.Equal(1, harness.Dialogs.Calls.Count(call => call == "error:Access denied"));
            Assert.False(panel.IsLoading);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- navigation selection --------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NavigateUp_SelectsTheFolderWeCameFrom()
    {
        using var dir = new TempDir();
        dir.Dir("aaa");
        var sub = dir.Dir("sub");
        dir.Dir("zzz");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(sub);
        FileSystemEntry? revealed = null;
        panel.SelectionRevealRequested += (_, entry) => revealed = entry;

        await panel.NavigateUpCommand.ExecuteAsync(null);

        Assert.Equal(dir.Path, panel.CurrentPath);
        Assert.Equal("sub", panel.SelectedEntry?.Name);
        Assert.Equal(["sub"], panel.SelectedEntries.Select(entry => entry.Name));
        Assert.Same(panel.SelectedEntry, revealed);
        Assert.Equal(2, harness.Settings.Settings.NavigationHistory.Count);
    }

    // ---- M6: stale loads are dropped -----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task StaleNavigation_IsDiscarded()
    {
        using var dir = new TempDir();
        var slow = dir.Dir("slow");
        dir.File(Path.Combine("slow", "slow.txt"));
        var fast = dir.Dir("fast");
        dir.File(Path.Combine("fast", "fast.txt"));
        var gate = new TaskCompletionSource();
        var fileSystem = new DelegatingFileSystem(RealFileSystem)
        {
            BeforeList = path => path == slow ? gate.Task : Task.CompletedTask
        };
        var harness = new Harness(fileSystem);
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        var first = panel.NavigateToCommand.ExecuteAsync(slow);
        var second = panel.NavigateToCommand.ExecuteAsync(fast);
        await second;
        Assert.Equal(fast, panel.CurrentPath);

        gate.SetResult();
        await first;

        Assert.Equal(fast, panel.CurrentPath);
        Assert.Equal(new[] { "..", "fast.txt" }, Names(panel));
        Assert.False(panel.IsLoading);
        Assert.DoesNotContain(harness.Settings.Settings.NavigationHistory, item => item.Path == slow);
    }

    // ---- L3: type-ahead --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task IncrementalSearch_CyclesThroughMatches()
    {
        using var dir = new TempDir();
        dir.File("apple");
        dir.File("avocado");
        dir.File("banana");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        Assert.True(panel.IncrementalSearch("a"));
        Assert.Equal("apple", panel.SelectedEntry?.Name);
        Assert.True(panel.IncrementalSearch("a"));
        Assert.Equal("avocado", panel.SelectedEntry?.Name);
        Assert.True(panel.IncrementalSearch("a"));
        Assert.Equal("apple", panel.SelectedEntry?.Name);
        Assert.True(panel.IncrementalSearch("b"));
        Assert.Equal("banana", panel.SelectedEntry?.Name);
        Assert.True(panel.IncrementalSearch("ap"));
        Assert.Equal("apple", panel.SelectedEntry?.Name);
        Assert.True(panel.IncrementalSearch("ap"));
        Assert.Equal("apple", panel.SelectedEntry?.Name);
        Assert.False(panel.IncrementalSearch("zz"));
        Assert.Equal("apple", panel.SelectedEntry?.Name);
        Assert.Equal(["apple"], panel.SelectedEntries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task SelectPath_SelectsAndReveals()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var b = dir.File("b.txt");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        FileSystemEntry? revealed = null;
        panel.SelectionRevealRequested += (_, entry) => revealed = entry;

        Assert.True(panel.SelectPath(b));
        Assert.Equal(b, panel.SelectedEntry?.FullPath);
        Assert.Same(panel.SelectedEntry, revealed);
        Assert.Equal([panel.SelectedEntry!], panel.SelectedEntries);
        Assert.False(panel.SelectPath(Path.Combine(dir.Path, "missing.txt")));
        Assert.Equal(b, panel.SelectedEntry?.FullPath);
    }

    // ---- filter, status, favorites, operations -------------------------------------------------------------

    [AvaloniaFact]
    public async Task Filter_NarrowsTheListInPlace()
    {
        using var dir = new TempDir();
        dir.File("report.txt");
        dir.File("notes.md");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        var report = panel.FilteredEntries.Single(entry => entry.Name == "report.txt");

        panel.ToggleSearchCommand.Execute(null);
        panel.SearchFilter = "REP";
        Assert.True(panel.IsSearchActive);
        Assert.Equal(new[] { "..", "report.txt" }, Names(panel));
        Assert.Same(report, panel.FilteredEntries[1]);
        Assert.Contains("1 file", panel.StatusText);

        panel.ClearSearchCommand.Execute(null);
        Assert.False(panel.IsSearchActive);
        Assert.Equal(new[] { "..", "notes.md", "report.txt" }, Names(panel));
    }

    [AvaloniaFact]
    public async Task Status_ReportsCountsAndSelection()
    {
        using var dir = new TempDir();
        dir.Dir("folder");
        var a = dir.File("a.txt", "x");
        dir.File("b.txt", "xx");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        Assert.StartsWith("1 folder, 2 files", panel.StatusText);
        Assert.DoesNotContain("selected", panel.StatusText);

        panel.SelectPath(a);
        Assert.Contains("1 selected", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task ToggleFavorite_UpdatesTheStarTheListAndTheStatus()
    {
        using var dir = new TempDir();
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        var changed = 0;
        panel.FavoritesChanged += (_, _) => changed++;

        await panel.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.True(panel.IsFavorite);
        Assert.Contains(panel.Favorites, favorite => favorite.Path == dir.Path);
        Assert.Equal("Added to favorites.", panel.StatusText);

        await panel.ToggleFavoriteCommand.ExecuteAsync(null);
        Assert.False(panel.IsFavorite);
        Assert.Empty(panel.Favorites);
        Assert.Equal("Removed from favorites.", panel.StatusText);
        Assert.Equal(2, changed);
    }

    [AvaloniaFact]
    public async Task Delete_GoesThroughTheOperationsServiceAndReselectsTheNextRow()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var b = dir.File("b.txt");
        dir.File("c.txt");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        panel.SelectPath(b);

        harness.Dialogs.DeleteAnswer = DeleteChoice.Cancel;
        await panel.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.True(File.Exists(b));

        harness.Dialogs.DeleteAnswer = DeleteChoice.Trash;
        await panel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Contains("delete:1:trash:default", harness.Dialogs.Calls);
        Assert.Equal([b], harness.Trash.Trashed);
        Assert.False(File.Exists(b));
        Assert.Equal("c.txt", panel.SelectedEntry?.Name);
    }

    [AvaloniaFact]
    public async Task Drop_CopiesOrMovesThroughTheOperationsService()
    {
        using var dir = new TempDir();
        var source = dir.Dir("source");
        var copied = dir.File(Path.Combine("source", "copied.txt"), "c");
        var moved = dir.File(Path.Combine("source", "moved.txt"), "m");
        var target = dir.Dir("target");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(target);

        await panel.DropAsync([copied], move: false);
        await panel.DropAsync([moved], move: true);

        Assert.True(File.Exists(copied));
        Assert.False(File.Exists(moved));
        Assert.Equal(new[] { "..", "copied.txt", "moved.txt" }, Names(panel));
        Assert.Contains(source, source);
    }

    [AvaloniaFact]
    public async Task Watcher_RefreshesAfterAnExternalChange()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var harness = new Harness();
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        Assert.Single(harness.Settings.Settings.NavigationHistory);

        dir.File("b.txt");
        await WaitUntilAsync(() => panel.FilteredEntries.Any(entry => entry.Name == "b.txt"), timeoutMs: 8000);

        Assert.Contains(panel.FilteredEntries, entry => entry.Name == "b.txt");
        Assert.Single(harness.Settings.Settings.NavigationHistory);
        panel.Dispose();
    }

    [AvaloniaFact]
    public async Task NavigateTo_FilePath_KeepsWatchingTheFolder()
    {
        using var dir = new TempDir();
        var file = dir.File("doc.txt");
        var fileSystem = new DelegatingFileSystem(RealFileSystem);
        var harness = new Harness(fileSystem);
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);

        panel.EditablePath = file;
        await panel.NavigateToPathCommand.ExecuteAsync(null);
        Assert.Equal([file], fileSystem.Opened);
        Assert.Equal(dir.Path, panel.CurrentPath);

        dir.File("added.txt");
        await WaitUntilAsync(() => panel.FilteredEntries.Any(entry => entry.Name == "added.txt"), timeoutMs: 8000);

        Assert.Contains(panel.FilteredEntries, entry => entry.Name == "added.txt");
        panel.Dispose();
    }

    // ---- H3: unexpected failures are reported, never thrown ----------------------------------------------

    [AvaloniaFact]
    public async Task UnexpectedListingFailures_AreReportedInsteadOfThrown()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var other = dir.Dir("other");
        var fileSystem = new DelegatingFileSystem(RealFileSystem);
        var harness = new Harness(fileSystem);
        var panel = harness.Panel;
        await panel.InitializeAsync(dir.Path);
        fileSystem.ListFailure = new InvalidOperationException("boom");

        await panel.NavigateToCommand.ExecuteAsync(other);

        Assert.Equal(dir.Path, panel.CurrentPath);
        Assert.Equal(dir.Path, panel.EditablePath);
        Assert.Equal(new[] { "..", "other", "a.txt" }, Names(panel));
        Assert.StartsWith("Cannot open", panel.StatusText);
        Assert.Contains("boom", panel.StatusText);
        Assert.Equal(["error:Navigate"], harness.Dialogs.Calls);
        Assert.False(panel.IsLoading);

        await panel.RefreshAsync();

        Assert.StartsWith("Refresh failed", panel.StatusText);
        Assert.Equal(new[] { "..", "other", "a.txt" }, Names(panel));
        Assert.False(panel.IsLoading);

        fileSystem.ListFailure = null;
        await panel.NavigateToCommand.ExecuteAsync(other);
        Assert.Equal(other, panel.CurrentPath);
    }

    // ---- audit regression: a refresh never cancels the listing a command is waiting for -------------------

    [AvaloniaFact]
    public async Task RefreshAsync_DoesNotCancelAListingAlreadyInFlight()
    {
        // The watcher's debounce timer fires a refresh right after a delete or rename. A refresh used to cancel
        // the one already running, so the command's own "await RefreshAsync()" returned with the old rows still
        // listed: it then re-selected a row that no longer existed and lost the cursor when the newer listing
        // arrived. A refresh now queues behind whatever is in flight instead of cancelling it.
        using var dir = new TempDir();
        dir.File("a.txt");

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileSystem = new DelegatingFileSystem(RealFileSystem);
        var harness = new Harness(fileSystem);
        var panel = harness.Panel;

        // The first load must itself be a refresh: a refresh was always allowed to queue behind a navigation,
        // so initializing here and holding that listing would not tell the two behaviours apart.
        await panel.InitializeAsync(dir.Path);
        var listingsBeforeHold = fileSystem.ListingTokens.Count;

        var held = true;
        fileSystem.BeforeList = async _ =>
        {
            if (held)
            {
                held = false;
                reached.TrySetResult();
                await release.Task;
            }
        };

        var first = panel.RefreshAsync();
        await reached.Task;

        // A second refresh arrives while the first listing is still running.
        var second = panel.RefreshAsync();
        release.TrySetResult();
        await first;
        await second;

        // The listing the first caller waited for was never cancelled, so its result is the one that applied.
        Assert.True(fileSystem.ListingTokens.Count > listingsBeforeHold);
        Assert.False(fileSystem.ListingTokens[listingsBeforeHold].IsCancellationRequested);
        Assert.Contains("a.txt", Names(panel));
    }

    // ---- selection by pattern, copy path, terminal -----------------------------------------------------------

    [AvaloniaFact]
    public async Task SelectByPattern_AddsTheMatchesAndKeepsWhatWasSelected()
    {
        using var dir = new TempDir();
        dir.File("a.cs");
        dir.File("b.cs");
        dir.File("c.txt");
        dir.File("d.md");
        var harness = new Harness();
        harness.Dialogs.InputAnswer = "*.cs";
        await harness.Panel.InitializeAsync(dir.Path);

        await harness.Panel.SelectByPatternCommand.ExecuteAsync(null);

        Assert.Equal(["a.cs", "b.cs"], harness.Panel.SelectedEntries.Select(entry => entry.Name).Order());
        Assert.DoesNotContain(harness.Panel.SelectedEntries, entry => entry.EntryType == FileSystemEntryType.ParentDirectory);

        // A second mask adds to the selection rather than replacing it.
        harness.Dialogs.InputAnswer = "*.md";
        await harness.Panel.SelectByPatternCommand.ExecuteAsync(null);

        Assert.Equal(["a.cs", "b.cs", "d.md"], harness.Panel.SelectedEntries.Select(entry => entry.Name).Order());
    }

    [AvaloniaFact]
    public async Task UnselectByPattern_TakesTheMatchesOut()
    {
        using var dir = new TempDir();
        dir.File("a.cs");
        dir.File("b.txt");
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        SelectEverything(harness.Panel);

        harness.Dialogs.InputAnswer = "*.txt";
        await harness.Panel.UnselectByPatternCommand.ExecuteAsync(null);

        Assert.Equal(["a.cs"], harness.Panel.SelectedEntries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task SelectByPattern_CancelledLeavesTheSelectionAlone()
    {
        using var dir = new TempDir();
        dir.File("a.cs");
        var harness = new Harness();
        harness.Dialogs.InputAnswer = null;
        await harness.Panel.InitializeAsync(dir.Path);

        await harness.Panel.SelectByPatternCommand.ExecuteAsync(null);

        Assert.DoesNotContain(harness.Panel.SelectedEntries, entry => entry.Name == "a.cs");
    }

    [AvaloniaFact]
    public async Task InvertSelection_SwapsTheSelectionAndSkipsTheParentEntry()
    {
        using var dir = new TempDir();
        var child = dir.Dir("child");
        dir.File("child/a.txt");
        dir.File("child/b.txt");
        var harness = new Harness();
        await harness.Panel.InitializeAsync(child);
        harness.Panel.SelectedEntries.Add(harness.Panel.FilteredEntries.Single(entry => entry.Name == "a.txt"));

        harness.Panel.InvertSelectionCommand.Execute(null);

        Assert.Equal(["b.txt"], harness.Panel.SelectedEntries.Select(entry => entry.Name));
        Assert.DoesNotContain(harness.Panel.SelectedEntries, entry => entry.EntryType == FileSystemEntryType.ParentDirectory);
    }

    [AvaloniaFact]
    public async Task CopyPath_WritesTheSelectionOnePerLine()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.File("b.txt");
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        SelectEverything(harness.Panel);

        await harness.Panel.CopyPathCommand.ExecuteAsync(null);

        Assert.Equal(string.Join(Environment.NewLine, [a, b]), harness.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task CopyPath_FallsBackToTheCurrentFolder()
    {
        using var dir = new TempDir();
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);

        await harness.Panel.CopyPathCommand.ExecuteAsync(null);

        Assert.Equal(dir.Path, harness.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task OpenTerminal_UsesTheSelectedFolderOrTheCurrentOne()
    {
        using var dir = new TempDir();
        var child = dir.Dir("child");
        var fileSystem = new DelegatingFileSystem(RealFileSystem);
        var harness = new Harness(fileSystem);
        await harness.Panel.InitializeAsync(dir.Path);

        await harness.Panel.OpenTerminalCommand.ExecuteAsync(null);
        Assert.Equal([dir.Path], fileSystem.TerminalsOpened);

        harness.Panel.SelectedEntry = harness.Panel.FilteredEntries.Single(entry => entry.Name == "child");
        await harness.Panel.OpenTerminalCommand.ExecuteAsync(null);
        Assert.Equal([dir.Path, child], fileSystem.TerminalsOpened);
    }

    // ---- folder size ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CalculateFolderSize_ShowsTheTotalOnTheRow()
    {
        using var dir = new TempDir();
        dir.File("box/a.txt", new string('x', 100));
        dir.File("box/deep/b.txt", new string('y', 50));
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        harness.Panel.SelectedEntry = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");

        await harness.Panel.CalculateFolderSizeCommand.ExecuteAsync(null);

        var box = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");
        Assert.Equal(150, box.CalculatedSize);
    }

    [AvaloniaFact]
    public async Task CalculateFolderSize_SurvivesARefresh()
    {
        using var dir = new TempDir();
        dir.File("box/a.txt", new string('x', 100));
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        harness.Panel.SelectedEntry = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");
        await harness.Panel.CalculateFolderSizeCommand.ExecuteAsync(null);

        await harness.Panel.RefreshAsync();

        var box = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");
        Assert.Equal(100, box.CalculatedSize);
    }

    [AvaloniaFact]
    public async Task CalculateFolderSize_IsForgottenOnNavigation()
    {
        using var dir = new TempDir();
        var other = dir.Dir("other");
        dir.File("box/a.txt", new string('x', 100));
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        harness.Panel.SelectedEntry = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");
        await harness.Panel.CalculateFolderSizeCommand.ExecuteAsync(null);

        await harness.Panel.NavigateToAsync(other);
        await harness.Panel.NavigateToAsync(dir.Path);

        var box = harness.Panel.FilteredEntries.Single(entry => entry.Name == "box");
        Assert.Null(box.CalculatedSize);
    }

    [AvaloniaFact]
    public async Task CalculateFolderSize_SaysSoWhenNothingIsAFolder()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var harness = new Harness();
        await harness.Panel.InitializeAsync(dir.Path);
        harness.Panel.SelectedEntry = harness.Panel.FilteredEntries.Single(entry => entry.Name == "a.txt");

        await harness.Panel.CalculateFolderSizeCommand.ExecuteAsync(null);

        Assert.Contains("Select a folder", harness.Panel.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
