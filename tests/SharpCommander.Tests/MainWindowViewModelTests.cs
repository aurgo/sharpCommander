using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>Tabs, panels, clipboard and theme handling of the main view model, with fakes for dialogs and settings.</summary>
public class MainWindowViewModelTests
{
    private static readonly FileSystemService FileSystem = new();

    private sealed class Harness : IDisposable
    {
        public FakeSettingsService Settings { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeClipboardService Clipboard { get; } = new();
        public FakeTrashService Trash { get; } = new();
        public MainWindowViewModel ViewModel { get; }

        public Harness()
        {
            var operations = new FileOperationsService(FileSystem, Dialogs, Trash);
            ViewModel = new MainWindowViewModel(FileSystem, Settings, Dialogs, Clipboard, operations, Trash, new ThemeService());
        }

        public void Dispose() => ViewModel.Dispose();
    }

    private static void Select(FilePanelViewModel panel, string name)
    {
        var entry = panel.FilteredEntries.Single(e => e.Name == name);
        panel.SelectedEntry = entry;
        panel.SelectedEntries.Clear();
        panel.SelectedEntries.Add(entry);
    }

    // ---- M12: panels belong to tabs ---------------------------------------------------------------------

    [AvaloniaFact]
    public void Panels_AreTheCurrentTabsPanels()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        var tab = Assert.Single(vm.Tabs);
        Assert.Same(tab, vm.CurrentTab);
        Assert.Same(tab.LeftPanel, vm.LeftPanel);
        Assert.Same(tab.RightPanel, vm.RightPanel);
        Assert.Same(tab.LeftPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public void SetActivePanel_GoesThroughTheTab()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        vm.SetActivePanel(vm.RightPanel);

        Assert.Same(vm.RightPanel, vm.ActivePanel);
        Assert.Same(vm.RightPanel, vm.CurrentTab!.ActivePanel);
    }

    // ---- H6: tabs -----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NewTab_OpensWithTheCurrentPathsAndBecomesCurrent()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(left, right);

        await vm.NewTabCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(vm.Tabs[1], vm.CurrentTab);
        Assert.Same(vm.CurrentTab.LeftPanel, vm.LeftPanel);
        Assert.Same(vm.CurrentTab.RightPanel, vm.RightPanel);
        Assert.Equal(left, vm.LeftPanel.CurrentPath);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public void CloseTab_RefusesTheLastTab()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        vm.CloseCurrentTabCommand.Execute(null);
        vm.CloseTabCommand.Execute(vm.CurrentTab);

        Assert.Single(vm.Tabs);
        Assert.Contains("last tab", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task CloseTab_SwitchesToTheNeighbourAndDisposesIt()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var first = vm.CurrentTab!;
        await vm.NewTabCommand.ExecuteAsync(null);
        await vm.NewTabCommand.ExecuteAsync(null);
        var third = vm.CurrentTab!;

        vm.CloseCurrentTabCommand.Execute(null);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(vm.Tabs[1], vm.CurrentTab);
        Assert.DoesNotContain(third, vm.Tabs);

        vm.SelectTabCommand.Execute(first);
        Assert.Same(first, vm.CurrentTab);
        vm.CloseTabCommand.Execute(vm.Tabs[1]);
        Assert.Same(first, vm.CurrentTab);
        Assert.Same(first.LeftPanel, vm.LeftPanel);
    }

    [AvaloniaFact]
    public async Task NextAndPreviousTab_Cycle()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.NewTabCommand.ExecuteAsync(null);

        vm.NextTabCommand.Execute(null);
        Assert.Same(vm.Tabs[0], vm.CurrentTab);
        vm.PreviousTabCommand.Execute(null);
        Assert.Same(vm.Tabs[1], vm.CurrentTab);
    }

    [AvaloniaFact]
    public async Task TabTitle_FollowsTheActivePanelsFolder()
    {
        using var dir = new TempDir();
        var projects = dir.Dir("Projects");
        var photos = dir.Dir("Photos");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var tab = vm.CurrentTab!;

        await tab.InitializeAsync(dir.Path, projects);
        Assert.Equal(Path.GetFileName(dir.Path), tab.Title);

        vm.SetActivePanel(vm.RightPanel);
        Assert.Equal("Projects", tab.Title);

        await vm.RightPanel.NavigateToCommand.ExecuteAsync(photos);
        Assert.Equal("Photos", tab.Title);

        await vm.RightPanel.NavigateToCommand.ExecuteAsync(string.Empty);
        Assert.Equal("Computer", tab.Title);
    }

    [AvaloniaFact]
    public void TabTitle_ForRootsIsThePathItself()
    {
        Assert.Equal("Computer", TabViewModel.TitleFor(null));
        Assert.Equal("Computer", TabViewModel.TitleFor(string.Empty));
        Assert.Equal("Documents", TabViewModel.TitleFor(Path.Combine(Path.GetTempPath(), "Documents") + Path.DirectorySeparatorChar));
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root, TabViewModel.TitleFor(root));
    }

    // ---- M10 / M7: clipboard through the operations service --------------------------------------------

    [AvaloniaFact]
    public async Task Paste_AfterCut_MovesAndClearsCutMode()
    {
        using var dir = new TempDir();
        var source = dir.Dir("src");
        var file = dir.File(Path.Combine("src", "a.txt"), "moved");
        var destination = dir.Dir("dst");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(source, destination);

        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");
        await vm.CutToClipboardCommand.ExecuteAsync(null);
        Assert.True(harness.Clipboard.IsCutMode);

        vm.SetActivePanel(vm.RightPanel);
        await vm.PasteFromClipboardCommand.ExecuteAsync(null);

        Assert.False(File.Exists(file));
        Assert.Equal("moved", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.False(harness.Clipboard.IsCutMode);
        Assert.Empty(harness.Clipboard.Paths);
        Assert.Contains(vm.RightPanel.FilteredEntries, e => e.Name == "a.txt");
        Assert.DoesNotContain(vm.LeftPanel.FilteredEntries, e => e.Name == "a.txt");
        Assert.Contains("Moved 1 item", vm.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Paste_AfterCopy_CopiesAndKeepsTheClipboard()
    {
        using var dir = new TempDir();
        var source = dir.Dir("src");
        var file = dir.File(Path.Combine("src", "a.txt"), "copied");
        var destination = dir.Dir("dst");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(source, destination);

        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");
        await vm.CopyToClipboardCommand.ExecuteAsync(null);
        vm.SetActivePanel(vm.RightPanel);
        await vm.PasteFromClipboardCommand.ExecuteAsync(null);

        Assert.True(File.Exists(file));
        Assert.Equal("copied", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.Equal([file], harness.Clipboard.Paths);
    }

    // ---- C1/C3 through the commands ----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Move_WithBothPanelsOnTheSameFolder_DeletesNothing()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "safe");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");

        await vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal("safe", File.ReadAllText(file));
        Assert.Single(harness.Dialogs.ReportedErrors);
    }

    [AvaloniaFact]
    public async Task Delete_AsksAndReselectsTheNextEntry()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        dir.File("b.txt");
        dir.File("c.txt");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "b.txt");
        harness.Dialogs.DeleteAnswer = DeleteChoice.Permanent;

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("delete:1:trash:default", harness.Dialogs.Calls);
        Assert.False(File.Exists(Path.Combine(dir.Path, "b.txt")));
        Assert.Equal("c.txt", vm.LeftPanel.SelectedEntry?.Name);

        harness.Dialogs.DeleteAnswer = DeleteChoice.Cancel;
        await vm.DeletePermanentCommand.ExecuteAsync(null);
        Assert.Contains("delete:1:trash:permanent", harness.Dialogs.Calls);
        Assert.True(File.Exists(Path.Combine(dir.Path, "c.txt")));
    }

    [AvaloniaFact]
    public async Task NewFolder_CreatesAndSelectsIt()
    {
        using var dir = new TempDir();
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        harness.Dialogs.InputAnswer = "Created";
        FileSystemEntry? revealed = null;
        vm.LeftPanel.SelectionRevealRequested += (_, entry) => revealed = entry;

        await vm.NewFolderCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(Path.Combine(dir.Path, "Created")));
        Assert.Equal("Created", vm.LeftPanel.SelectedEntry?.Name);
        Assert.Same(vm.LeftPanel.SelectedEntry, revealed);
        Assert.Equal([vm.LeftPanel.SelectedEntry!], vm.LeftPanel.SelectedEntries);
    }

    // ---- M11: theme and state ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void SetTheme_AppliesAndPersists()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;

        try
        {
            vm.SetThemeCommand.Execute("dark");

            Assert.Equal("Dark", vm.CurrentTheme);
            Assert.Equal("Dark", harness.Settings.Settings.Theme);
            Assert.Equal(1, harness.Settings.RequestSaveCount);
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }

    [AvaloniaFact]
    public async Task SaveState_PersistsPanelsFavoritesPanelThemeAndWindow()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(left, right);
        vm.ToggleFavoritesPanelCommand.Execute(null);
        vm.RememberWindowBounds(1280, 720, isMaximized: false);

        await vm.ShutdownAsync();

        var settings = harness.Settings.Settings;
        Assert.Equal(left, settings.LastLeftPanelPath);
        Assert.Equal(right, settings.LastRightPanelPath);
        Assert.False(settings.FavoritesPanelVisible);
        Assert.Equal("System", settings.Theme);
        Assert.Equal(1280, settings.WindowWidth);
        Assert.Equal("Normal", settings.WindowState);
        Assert.Equal(1, harness.Settings.SaveCount);
        Assert.Equal(1, harness.Settings.FlushCount);
    }

    [AvaloniaFact]
    public void Version_ComesFromTheAssembly()
    {
        using var harness = new Harness();

        Assert.Matches(@"^\d+\.\d+\.\d+$", harness.ViewModel.Version);
    }

    [AvaloniaFact]
    public void Exit_RaisesExitRequested()
    {
        using var harness = new Harness();
        var raised = false;
        harness.ViewModel.ExitRequested += (_, _) => raised = true;

        harness.ViewModel.ExitCommand.Execute(null);

        Assert.True(raised);
    }

    [AvaloniaFact]
    public async Task Dispose_DisposesTabs()
    {
        var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.NewTabCommand.ExecuteAsync(null);

        vm.Dispose();

        Assert.Empty(vm.Tabs);
    }

    // ---- L8: favorites commands ---------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Favorites_RemoveRenameAndOpenInOtherPanel()
    {
        using var dir = new TempDir();
        var folder = dir.Dir("Fav");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        await harness.Settings.AddFavoriteAsync(folder);
        var favorite = harness.Settings.Settings.Favorites.Single();

        harness.Dialogs.InputAnswer = "Renamed";
        await vm.RenameFavoriteCommand.ExecuteAsync(favorite);
        Assert.Equal("Renamed", favorite.Name);
        Assert.Contains(vm.LeftPanel.Favorites, f => f.Name == "Renamed");

        vm.SetActivePanel(vm.LeftPanel);
        await vm.OpenFavoriteInOtherPanelCommand.ExecuteAsync(favorite);
        Assert.Equal(folder, vm.RightPanel.CurrentPath);

        await vm.RemoveFavoriteCommand.ExecuteAsync(favorite);
        Assert.Empty(harness.Settings.Settings.Favorites);
        Assert.Empty(vm.RightPanel.Favorites);
        Assert.False(vm.RightPanel.IsFavorite);
    }

    // ---- C1: cut and paste in the same folder --------------------------------------------------------------

    [AvaloniaFact]
    public async Task Paste_CutInTheSameFolder_DeletesNothing()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "safe");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");

        await vm.CutToClipboardCommand.ExecuteAsync(null);
        await vm.PasteFromClipboardCommand.ExecuteAsync(null);

        Assert.True(File.Exists(file));
        Assert.Equal("safe", File.ReadAllText(file));
        var error = Assert.Single(harness.Dialogs.ReportedErrors);
        Assert.Contains("same", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(vm.LeftPanel.FilteredEntries, entry => entry.Name == "a.txt");
        Assert.True(harness.Clipboard.IsCutMode); // nothing moved, so the cut is still pending
        Assert.DoesNotContain("clear", harness.Clipboard.Calls);
    }

    // ---- M3: a search result is opened in the active panel ------------------------------------------------

    [AvaloniaFact]
    public async Task AdvancedSearch_NavigatesToTheResultAndSelectsIt()
    {
        using var dir = new TempDir();
        var sub = dir.Dir("sub");
        dir.File(Path.Combine("sub", "a.txt"));
        var target = dir.File(Path.Combine("sub", "target.txt"));
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        harness.Dialogs.SearchAnswer = new FileSystemEntry { Name = "target.txt", FullPath = target, EntryType = FileSystemEntryType.File };
        FileSystemEntry? revealed = null;
        vm.LeftPanel.SelectionRevealRequested += (_, entry) => revealed = entry;

        await vm.ShowAdvancedSearchCommand.ExecuteAsync(null);

        Assert.Contains("search:" + dir.Path, harness.Dialogs.Calls);
        Assert.Equal(sub, vm.LeftPanel.CurrentPath);
        Assert.Equal(target, vm.LeftPanel.SelectedEntry?.FullPath);
        Assert.Equal([target], vm.LeftPanel.SelectedEntries.Select(entry => entry.FullPath));
        Assert.Same(vm.LeftPanel.SelectedEntry, revealed);
        Assert.Equal(dir.Path, vm.RightPanel.CurrentPath);

        harness.Dialogs.SearchAnswer = null;
        await vm.ShowAdvancedSearchCommand.ExecuteAsync(null);
        Assert.Equal(sub, vm.LeftPanel.CurrentPath);
    }

    // ---- M2: the panel is refreshed after a mass rename ----------------------------------------------------

    [AvaloniaFact]
    public async Task MassRename_RefreshesThePanelAfterApplying()
    {
        using var dir = new TempDir();
        dir.File("old.txt");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);

        await vm.ShowMassRenameCommand.ExecuteAsync(null);
        Assert.DoesNotContain(harness.Dialogs.Calls, call => call.StartsWith("massrename:", StringComparison.Ordinal));
        Assert.Contains("Select the files to rename", vm.StatusMessage);

        Select(vm.LeftPanel, "old.txt");
        harness.Dialogs.MassRenameAnswer = true;
        harness.Dialogs.MassRenameHandler = paths => File.Move(paths.Single(), Path.Combine(dir.Path, "new.txt"));

        await vm.ShowMassRenameCommand.ExecuteAsync(null);

        Assert.Contains("massrename:1", harness.Dialogs.Calls);
        Assert.Equal(new[] { "..", "new.txt" }, vm.LeftPanel.FilteredEntries.Select(entry => entry.Name));
    }

    // ---- M12: favorites subscriptions follow the current tab ------------------------------------------------

    [AvaloniaFact]
    public async Task FavoritesChanged_InTheCurrentTab_UpdatesEveryTab()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.CurrentTab!.InitializeAsync(left, right);
        var first = vm.CurrentTab;
        await vm.NewTabCommand.ExecuteAsync(null);
        var second = vm.CurrentTab!;
        Assert.NotSame(first, second);

        vm.SetActivePanel(vm.LeftPanel);
        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.True(harness.Settings.IsFavorite(left));
        Assert.True(second.LeftPanel.IsFavorite);
        Assert.Contains(second.RightPanel.Favorites, favorite => favorite.Path == left);
        Assert.Contains(first.LeftPanel.Favorites, favorite => favorite.Path == left);
        Assert.True(first.LeftPanel.IsFavorite);

        vm.SelectTabCommand.Execute(first);
        vm.SetActivePanel(vm.LeftPanel);
        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(harness.Settings.IsFavorite(left));
        Assert.Empty(second.LeftPanel.Favorites);
        Assert.False(second.LeftPanel.IsFavorite);
        Assert.Empty(second.RightPanel.Favorites);
    }

    // ---- H3: commands report failures instead of throwing --------------------------------------------------

    private sealed class ThrowingClipboard : IClipboardService
    {
        public bool IsCutMode => false;
        public Task CopyAsync(IEnumerable<FileSystemEntry> items) => throw new InvalidOperationException("clipboard boom");
        public Task CutAsync(IEnumerable<FileSystemEntry> items) => throw new InvalidOperationException("clipboard boom");
        public Task<IReadOnlyList<string>> GetPathsAsync() => throw new InvalidOperationException("clipboard boom");
        public Task ClearAsync() => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Commands_ReportFailuresInsteadOfThrowing()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        using var vm = new MainWindowViewModel(FileSystem, new FakeSettingsService(), dialogs, new ThrowingClipboard(), new FileOperationsService(FileSystem, dialogs, trash), trash, new ThemeService());
        await vm.CurrentTab!.InitializeAsync(dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");

        await vm.CopyToClipboardCommand.ExecuteAsync(null);
        await vm.PasteFromClipboardCommand.ExecuteAsync(null);

        Assert.Equal(["error:Copy", "error:Paste"], dialogs.Calls);
        Assert.Equal(["clipboard boom", "clipboard boom"], dialogs.ErrorMessages);
        Assert.Contains("Paste failed: clipboard boom", vm.StatusMessage);
    }
}
