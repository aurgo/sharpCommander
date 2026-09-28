using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Localization;
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
        public FakeUpdateService Updates { get; } = new();
        public FakeSpaceAnalyzerService SpaceAnalyzer { get; } = new();
        public MainWindowViewModel ViewModel { get; }

        public Harness()
        {
            var operations = new FileOperationsService(FileSystem, Dialogs, Trash);
            ViewModel = new MainWindowViewModel(FileSystem, Settings, Dialogs, Clipboard, operations, Trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), Updates, SpaceAnalyzer);
        }

        public void Dispose() => ViewModel.Dispose();
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

    /// <summary>Puts the two panes on the given folders; the panels are shared by every tab.</summary>
    private static Task GoAsync(MainWindowViewModel vm, string leftPath, string rightPath)
    {
        return Task.WhenAll(vm.LeftPanel.InitializeAsync(leftPath), vm.RightPanel.InitializeAsync(rightPath));
    }

    private static void Select(FilePanelViewModel panel, string name)
    {
        var entry = panel.FilteredEntries.Single(e => e.Name == name);
        panel.SelectedEntry = entry;
        panel.SelectedEntries.Clear();
        panel.SelectedEntries.Add(entry);
    }

    // ---- M12: the two panes are shared, a tab is a folder remembered for one of them --------------------

    [AvaloniaFact]
    public void EachPane_StartsWithOneTabOfItsOwn()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal(PanelSide.Left, Assert.Single(vm.LeftTabs).Side);
        Assert.Equal(PanelSide.Right, Assert.Single(vm.RightTabs).Side);
        Assert.Same(vm.LeftTabs[0], vm.CurrentTab);
        Assert.Same(vm.LeftPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public async Task SwitchingTab_LeavesTheOppositePaneWhereItWas()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var firstLeftTab = vm.LeftTabs[0];

        // A second left-hand tab, then move the right pane somewhere of its own.
        await vm.OpenInNewTabAsync(vm.LeftPanel, vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target"));
        await vm.RightPanel.NavigateToCommand.ExecuteAsync(dir.Path);
        Assert.Equal(target, vm.LeftPanel.CurrentPath);
        Assert.Equal(dir.Path, vm.RightPanel.CurrentPath);

        await vm.SelectTabCommand.ExecuteAsync(firstLeftTab);

        // Only the left pane moved back; the right one is untouched.
        Assert.Equal(left, vm.LeftPanel.CurrentPath);
        Assert.Equal(dir.Path, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task SwitchingTab_RemembersWhereItsOwnPaneWas()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var firstLeftTab = vm.LeftTabs[0];

        await vm.OpenInNewTabAsync(vm.LeftPanel, vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target"));
        var secondLeftTab = vm.LeftTabs[1];

        await vm.SelectTabCommand.ExecuteAsync(firstLeftTab);
        Assert.Equal(left, vm.LeftPanel.CurrentPath);

        await vm.SelectTabCommand.ExecuteAsync(secondLeftTab);
        Assert.Equal(target, vm.LeftPanel.CurrentPath);
    }

    [AvaloniaFact]
    public void SetActivePanel_GoesThroughTheTab()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        vm.SetActivePanel(vm.RightPanel);

        Assert.Same(vm.RightPanel, vm.ActivePanel);
        Assert.Equal(PanelSide.Right, vm.CurrentTab!.Side);
    }

    // ---- H6: tabs -----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NewTab_OpensOnTheActivePaneAndBecomesCurrent()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);

        await vm.NewTabCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Same(vm.LeftTabs[1], vm.CurrentTab);
        Assert.Equal(PanelSide.Left, vm.CurrentTab!.Side);
        Assert.Equal(left, vm.LeftPanel.CurrentPath);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_PutsTheFolderInTheClickedPanelAndKeepsTheOther()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var entry = vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target");

        await vm.OpenInNewTabAsync(vm.LeftPanel, entry);

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Same(vm.LeftTabs[1], vm.CurrentTab);
        Assert.Equal(target, vm.LeftPanel.CurrentPath);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_OpensIntoTheRightPanelWhenThatIsTheActiveOne()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("right/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        vm.SetActivePanel(vm.RightPanel);
        var entry = vm.RightPanel.FilteredEntries.Single(e => e.Name == "target");

        await vm.OpenInNewTabAsync(vm.RightPanel, entry);

        Assert.Equal(2, vm.RightTabs.Count);
        Assert.Equal(left, vm.LeftPanel.CurrentPath);
        Assert.Equal(target, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_LeavesTheOriginalTabWhereItWas()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var first = vm.Tabs[0];
        var entry = vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target");

        await vm.OpenInNewTabAsync(vm.LeftPanel, entry);

        Assert.Equal(left, first.Path);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_RefusesAFile()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        dir.File("left/note.txt", "x");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);
        var entry = vm.LeftPanel.FilteredEntries.Single(e => e.Name == "note.txt");

        await vm.OpenInNewTabAsync(vm.LeftPanel, entry);

        Assert.Single(vm.LeftTabs);
        Assert.Equal(left, vm.LeftPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_FollowsTheParentEntryToTheParentFolder()
    {
        using var dir = new TempDir();
        var parent = dir.Dir("parent");
        var child = dir.Dir("parent/child");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, child, child);
        var entry = vm.LeftPanel.FilteredEntries.Single(e => e.EntryType == FileSystemEntryType.ParentDirectory);

        await vm.OpenInNewTabAsync(vm.LeftPanel, entry);

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Equal(parent, vm.LeftPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_UsesTheClickedPanelNotTheFocusedOne()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        vm.SetActivePanel(vm.RightPanel);
        var entry = vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target");

        await vm.OpenInNewTabAsync(vm.LeftPanel, entry);

        Assert.Equal(target, vm.LeftPanel.CurrentPath);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task OpenInNewTab_MakesTheOwningSideTheActivePanel()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("right/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var entry = vm.RightPanel.FilteredEntries.Single(e => e.Name == "target");

        await vm.OpenInNewTabAsync(vm.RightPanel, entry);

        Assert.Same(vm.RightPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public async Task NewTab_KeepsTheSideThatHadTheFocus()
    {
        using var dir = new TempDir();
        var path = dir.Dir("both");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, path, path);
        vm.SetActivePanel(vm.RightPanel);

        await vm.NewTabCommand.ExecuteAsync(null);

        Assert.Same(vm.RightPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public async Task SelectTab_ReturnsTheFocusToTheSideTheTabBelongsTo()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("right/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var first = vm.LeftTabs[0];
        var entry = vm.RightPanel.FilteredEntries.Single(e => e.Name == "target");

        // Opened from the right panel, so that tab belongs to the right side; the first one still belongs to the left.
        await vm.OpenInNewTabAsync(vm.RightPanel, entry);
        var second = vm.RightTabs[1];
        Assert.Same(vm.RightPanel, vm.ActivePanel);

        await vm.SelectTabCommand.ExecuteAsync(first);
        Assert.Same(vm.LeftPanel, vm.ActivePanel);

        await vm.SelectTabCommand.ExecuteAsync(second);
        Assert.Same(vm.RightPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public async Task CloseTab_RefusesTheLastTabOfAPane()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        await vm.CloseCurrentTabCommand.ExecuteAsync(null);
        await vm.CloseTabCommand.ExecuteAsync(vm.CurrentTab);

        Assert.Single(vm.LeftTabs);
        Assert.Single(vm.RightTabs);
        Assert.Contains("last tab", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task CloseTab_FallsBackToTheNeighbourOfTheSamePane()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var first = vm.LeftTabs[0];
        await vm.NewTabCommand.ExecuteAsync(null);
        await vm.NewTabCommand.ExecuteAsync(null);
        var third = vm.CurrentTab!;
        Assert.Equal(3, vm.LeftTabs.Count);

        await vm.CloseCurrentTabCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Same(vm.LeftTabs[1], vm.CurrentTab);
        Assert.DoesNotContain(third, vm.Tabs);

        await vm.SelectTabCommand.ExecuteAsync(first);
        Assert.Same(first, vm.CurrentTab);

        // Closing a tab that is not the one on screen leaves the current one alone.
        await vm.CloseTabCommand.ExecuteAsync(vm.LeftTabs[1]);
        Assert.Same(first, vm.CurrentTab);
        Assert.Same(vm.LeftPanel, vm.ActivePanel);
    }

    [AvaloniaFact]
    public async Task NextAndPreviousTab_CycleWithinTheActivePane()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.NewTabCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.LeftTabs.Count);

        await vm.NextTabCommand.ExecuteAsync(null);
        Assert.Same(vm.LeftTabs[0], vm.CurrentTab);
        await vm.PreviousTabCommand.ExecuteAsync(null);
        Assert.Same(vm.LeftTabs[1], vm.CurrentTab);

        // The right-hand tab is never reached from the left pane.
        Assert.DoesNotContain(vm.CurrentTab, vm.RightTabs);
    }

    [AvaloniaFact]
    public async Task TabTitle_FollowsTheFolderOfTheSideTheTabBelongsTo()
    {
        using var dir = new TempDir();
        var projects = dir.Dir("Projects");
        var photos = dir.Dir("Photos");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var tab = vm.CurrentTab!;

        await GoAsync(vm, dir.Path, projects);
        Assert.Equal(PanelSide.Left, tab.Side);
        Assert.Equal(Path.GetFileName(dir.Path), tab.Title);

        // Moving the focus to the other pane does not rename the tab: it still belongs to the left one.
        vm.SetActivePanel(vm.RightPanel);
        Assert.Equal(Path.GetFileName(dir.Path), tab.Title);

        await vm.LeftPanel.NavigateToCommand.ExecuteAsync(photos);
        Assert.Equal("Photos", tab.Title);

        await vm.LeftPanel.NavigateToCommand.ExecuteAsync(string.Empty);
        Assert.Equal("Computer", tab.Title);
    }

    [AvaloniaFact]
    public async Task TabTitle_OfARightHandTabFollowsTheRightPanel()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("right/Photos");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var entry = vm.RightPanel.FilteredEntries.Single(e => e.Name == "Photos");

        await vm.OpenInNewTabAsync(vm.RightPanel, entry);

        var tab = vm.CurrentTab!;
        Assert.Equal(PanelSide.Right, tab.Side);
        Assert.Equal("Photos", tab.Title);
    }

    [AvaloniaFact]
    public async Task Tabs_AreGroupedByTheSideTheyBelongTo()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("left/a");
        dir.Dir("right/b");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        var first = vm.LeftTabs[0];
        var firstRight = vm.RightTabs[0];

        await vm.OpenInNewTabAsync(vm.LeftPanel, vm.LeftPanel.FilteredEntries.Single(e => e.Name == "a"));
        var fromLeft = vm.CurrentTab!;
        await vm.OpenInNewTabAsync(vm.RightPanel, vm.RightPanel.FilteredEntries.Single(e => e.Name == "b"));
        var fromRight = vm.CurrentTab!;

        Assert.Equal([first, fromLeft], vm.LeftTabs);
        Assert.Equal([firstRight, fromRight], vm.RightTabs);
        Assert.Equal(4, vm.Tabs.Count);

        await vm.CloseTabCommand.ExecuteAsync(fromRight);

        Assert.Equal([firstRight], vm.RightTabs);
        Assert.Equal([first, fromLeft], vm.LeftTabs);
        Assert.Equal(3, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public async Task CurrentTab_IsTheOnlyOneMarkedCurrent()
    {
        using var dir = new TempDir();
        var path = dir.Dir("both");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, path, path);
        var first = vm.LeftTabs[0];

        await vm.NewTabCommand.ExecuteAsync(null);

        Assert.False(first.IsCurrent);
        Assert.True(vm.LeftTabs[1].IsCurrent);
        Assert.Single(vm.Tabs, tab => tab.IsCurrent);

        await vm.SelectTabCommand.ExecuteAsync(first);

        Assert.True(first.IsCurrent);
        Assert.False(vm.LeftTabs[1].IsCurrent);
        Assert.Single(vm.Tabs, tab => tab.IsCurrent);
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
        await GoAsync(vm, source, destination);

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
        await GoAsync(vm, source, destination);

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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, left, right);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, dir.Path, dir.Path);
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
        await GoAsync(vm, left, right);
        var first = vm.CurrentTab;
        await vm.NewTabCommand.ExecuteAsync(null);
        var second = vm.CurrentTab!;
        Assert.NotSame(first, second);

        vm.SetActivePanel(vm.LeftPanel);
        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.True(harness.Settings.IsFavorite(left));
        Assert.True(vm.LeftPanel.IsFavorite);
        Assert.Contains(vm.RightPanel.Favorites, favorite => favorite.Path == left);
        Assert.Contains(vm.LeftPanel.Favorites, favorite => favorite.Path == left);

        vm.SelectTabCommand.Execute(first);
        vm.SetActivePanel(vm.LeftPanel);
        await vm.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.False(harness.Settings.IsFavorite(left));
        Assert.Empty(vm.LeftPanel.Favorites);
        Assert.False(vm.LeftPanel.IsFavorite);
        Assert.Empty(vm.RightPanel.Favorites);
    }

    // ---- H3: commands report failures instead of throwing --------------------------------------------------

    private sealed class ThrowingClipboard : IClipboardService
    {
        public bool IsCutMode => false;
        public Task CopyAsync(IEnumerable<FileSystemEntry> items) => throw new InvalidOperationException("clipboard boom");
        public Task CutAsync(IEnumerable<FileSystemEntry> items) => throw new InvalidOperationException("clipboard boom");
        public Task<IReadOnlyList<string>> GetPathsAsync() => throw new InvalidOperationException("clipboard boom");
        public Task SetTextAsync(string text) => throw new InvalidOperationException("clipboard boom");
        public Task ClearAsync() => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Commands_ReportFailuresInsteadOfThrowing()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        using var vm = new MainWindowViewModel(FileSystem, new FakeSettingsService(), dialogs, new ThrowingClipboard(), new FileOperationsService(FileSystem, dialogs, trash), trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), new FakeUpdateService(), new FakeSpaceAnalyzerService());
        await GoAsync(vm, dir.Path, dir.Path);
        vm.SetActivePanel(vm.LeftPanel);
        Select(vm.LeftPanel, "a.txt");

        await vm.CopyToClipboardCommand.ExecuteAsync(null);
        await vm.PasteFromClipboardCommand.ExecuteAsync(null);

        Assert.Equal(["error:Copy", "error:Paste"], dialogs.Calls);
        Assert.Equal(["clipboard boom", "clipboard boom"], dialogs.ErrorMessages);
        Assert.Contains("Paste failed: clipboard boom", vm.StatusMessage);
    }

    // ---- tabs survive a session, folders can be measured -----------------------------------------------------

    [AvaloniaFact]
    public async Task Tabs_AreWrittenToTheSettings()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        await vm.OpenInNewTabAsync(vm.LeftPanel, vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target"));
        vm.TogglePinCommand.Execute(vm.LeftTabs[0]);

        vm.CaptureState();

        var saved = harness.Settings.Settings.Tabs;
        Assert.Equal(3, saved.Count);
        Assert.Equal([left, target], saved.Where(t => t.Side == "Left").Select(t => t.Path));
        Assert.Equal([right], saved.Where(t => t.Side == "Right").Select(t => t.Path));
        Assert.True(saved.Single(t => t.Path == left).IsPinned);
        Assert.True(saved.Single(t => t.Path == target).IsCurrent);
    }

    [AvaloniaFact]
    public async Task Tabs_AreRestoredFromTheSettings()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var other = dir.Dir("other");
        var right = dir.Dir("right");
        using var harness = new Harness();
        harness.Settings.Settings.LastLeftPanelPath = left;
        harness.Settings.Settings.LastRightPanelPath = right;
        harness.Settings.Settings.Tabs =
        [
            new TabState { Side = "Left", Path = left, IsPinned = true },
            new TabState { Side = "Left", Path = other, IsCurrent = true },
            new TabState { Side = "Right", Path = right, IsCurrent = true }
        ];
        var vm = harness.ViewModel;

        await vm.InitializeAsync();
        await WaitUntilAsync(() => vm.LeftPanel.CurrentPath == other);

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Single(vm.RightTabs);
        Assert.Equal([left, other], vm.LeftTabs.Select(tab => tab.Path));
        Assert.True(vm.LeftTabs[0].IsPinned);
        Assert.Equal(other, vm.LeftPanel.CurrentPath);
        Assert.Equal(right, vm.RightPanel.CurrentPath);
    }

    [AvaloniaFact]
    public async Task Tabs_RestoringSkipsFoldersThatAreGone()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        using var harness = new Harness();
        harness.Settings.Settings.LastLeftPanelPath = left;
        harness.Settings.Settings.LastRightPanelPath = left;
        harness.Settings.Settings.Tabs =
        [
            new TabState { Side = "Left", Path = left, IsCurrent = true },
            new TabState { Side = "Left", Path = Path.Combine(dir.Path, "deleted-since") }
        ];
        var vm = harness.ViewModel;

        await vm.InitializeAsync();

        Assert.Single(vm.LeftTabs);
        Assert.Equal(left, vm.LeftTabs[0].Path);
    }

    /// <summary>
    /// Closing the last window raises the lifetime's shutdown after the window is already closed, so one more
    /// save runs once the view model has been disposed and its tab collections emptied. That save used to
    /// capture the empty collections over the session that had just been written, and every start came up with
    /// a single tab per pane.
    /// </summary>
    [AvaloniaFact]
    public async Task Tabs_SurviveASaveThatRunsAfterTheWindowIsClosed()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        await vm.OpenInNewTabAsync(vm.LeftPanel, vm.LeftPanel.FilteredEntries.Single(e => e.Name == "target"));

        await vm.ShutdownAsync();
        vm.Dispose();
        await vm.SaveStateAsync();

        var saved = harness.Settings.Settings.Tabs;
        Assert.Equal(3, saved.Count);
        Assert.Equal(2, saved.Count(tab => tab.Side == "Left"));
        Assert.Equal([right], saved.Where(tab => tab.Side == "Right").Select(tab => tab.Path));
    }

    [AvaloniaFact]
    public async Task PinnedTab_KeepsItsFolderAndOpensAnotherTab()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var target = dir.Dir("left/target");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);
        var pinned = vm.LeftTabs[0];
        vm.TogglePinCommand.Execute(pinned);

        await vm.LeftPanel.NavigateToCommand.ExecuteAsync(target);

        Assert.Equal(left, pinned.Path);
        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Equal(target, vm.LeftTabs[1].Path);
    }

    [AvaloniaFact]
    public async Task PinnedTab_CannotBeClosed()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await vm.NewTabCommand.ExecuteAsync(null);
        var pinned = vm.LeftTabs[1];
        vm.TogglePinCommand.Execute(pinned);

        await vm.CloseTabCommand.ExecuteAsync(pinned);

        Assert.Contains(pinned, vm.LeftTabs);
        Assert.Contains("pinned", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task DuplicateTab_AddsASecondTabOnTheSameFolderAndPane()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);

        await vm.DuplicateTabCommand.ExecuteAsync(vm.LeftTabs[0]);

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Equal(left, vm.LeftTabs[1].Path);
        Assert.Equal(PanelSide.Left, vm.LeftTabs[1].Side);
        Assert.Same(vm.LeftTabs[1], vm.CurrentTab);
    }

    // ---- archives and folder comparison ----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Pack_WritesTheArchiveIntoTheOtherPanel()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "content");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");
        harness.Dialogs.InputAnswer = "bundle.zip";

        await vm.PackCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(right, "bundle.zip")));
        Assert.False(File.Exists(Path.Combine(left, "bundle.zip")));
    }

    [AvaloniaFact]
    public async Task Extract_UnpacksIntoAFolderOfItsOwnInTheOtherPanel()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("source/inner.txt", "inside");
        await new ZipArchiveService().CreateAsync([Path.Combine(dir.Path, "source")], Path.Combine(left, "bundle.zip"));
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "bundle.zip");

        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.Equal("inside", await File.ReadAllTextAsync(Path.Combine(right, "bundle", "source", "inner.txt")));
    }

    [AvaloniaFact]
    public async Task Extract_SaysSoWhenNothingSelectedIsAnArchive()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "x");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");

        await vm.ExtractCommand.ExecuteAsync(null);

        Assert.Contains(".zip", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task CompareDirectories_SelectsWhatDiffersOnEachSide()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/only-left.txt", "x");
        dir.File("right/only-right.txt", "x");
        dir.File("left/both.txt", "same");
        dir.File("right/both.txt", "same");
        File.SetLastWriteTimeUtc(Path.Combine(right, "both.txt"), File.GetLastWriteTimeUtc(Path.Combine(left, "both.txt")));
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);

        await vm.CompareDirectoriesCommand.ExecuteAsync(null);

        Assert.Equal(["only-left.txt"], vm.LeftPanel.SelectedEntries.Select(entry => entry.Name));
        Assert.Equal(["only-right.txt"], vm.RightPanel.SelectedEntries.Select(entry => entry.Name));
    }

    [AvaloniaFact]
    public async Task CompareDirectories_RefusesTheSameFolderOnBothSides()
    {
        using var dir = new TempDir();
        var same = dir.Dir("same");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, same, same);

        await vm.CompareDirectoriesCommand.ExecuteAsync(null);

        Assert.Contains("same folder", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Synchronize_CopiesTheMissingItemsAndLeavesTheExtrasAlone()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/new.txt", "fresh");
        dir.File("right/extra.txt", "keep me");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        vm.SetActivePanel(vm.LeftPanel);
        harness.Dialogs.ConfirmAnswer = true;

        await vm.SynchronizeDirectoriesCommand.ExecuteAsync(null);

        Assert.Equal("fresh", await File.ReadAllTextAsync(Path.Combine(right, "new.txt")));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(right, "extra.txt")));
        Assert.False(File.Exists(Path.Combine(left, "extra.txt")));
    }

    [AvaloniaFact]
    public async Task Synchronize_CopiesNothingWhenTheAnswerIsNo()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/new.txt", "fresh");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        vm.SetActivePanel(vm.LeftPanel);
        harness.Dialogs.ConfirmAnswer = false;

        await vm.SynchronizeDirectoriesCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(right, "new.txt")));
    }

    // ---- undo -------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Undo_MovesTheFilesBack()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "content");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");

        await vm.MoveCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(right, "a.txt")));

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(left, "a.txt")));
        Assert.False(File.Exists(Path.Combine(right, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Undo_DeletesTheCopiesItMadeAfterConfirmation()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "content");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");
        await vm.CopyCommand.ExecuteAsync(null);
        harness.Dialogs.ConfirmAnswer = true;

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(right, "a.txt")));
        Assert.True(File.Exists(Path.Combine(left, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Undo_OfACopyKeepsTheFilesWhenTheAnswerIsNo()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "content");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");
        await vm.CopyCommand.ExecuteAsync(null);
        harness.Dialogs.ConfirmAnswer = false;

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(right, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Undo_OfACopyNeverDeletesAFileThatWasAlreadyThere()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "new");
        dir.File("right/a.txt", "was already here");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");
        harness.Dialogs.ConflictAnswer = new ConflictResolution(ConflictAction.Overwrite);
        await vm.CopyCommand.ExecuteAsync(null);
        harness.Dialogs.ConfirmAnswer = true;

        await vm.UndoCommand.ExecuteAsync(null);

        // The destination name existed before the copy, so it is not the copy's to remove.
        Assert.True(File.Exists(Path.Combine(right, "a.txt")));
    }

    [AvaloniaFact]
    public async Task Undo_RenamesBack()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        dir.File("left/before.txt", "x");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);
        Select(vm.LeftPanel, "before.txt");
        harness.Dialogs.InputAnswer = "after.txt";
        await vm.RenameCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(left, "after.txt")));

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(left, "before.txt")));
        Assert.False(File.Exists(Path.Combine(left, "after.txt")));
    }

    [AvaloniaFact]
    public async Task Undo_RemovesAFolderItCreated()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);
        harness.Dialogs.InputAnswer = "brand-new";
        await vm.NewFolderCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(Path.Combine(left, "brand-new")));

        harness.Dialogs.ConfirmAnswer = true;
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(left, "brand-new")));
    }

    [AvaloniaFact]
    public async Task Undo_SkipsAnItemWhoseOldNameIsTakenAgain()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/a.txt", "moved away");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);
        Select(vm.LeftPanel, "a.txt");
        await vm.MoveCommand.ExecuteAsync(null);

        // Something else took the old name in the meantime.
        await File.WriteAllTextAsync(Path.Combine(left, "a.txt"), "a different file");

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal("a different file", await File.ReadAllTextAsync(Path.Combine(left, "a.txt")));
        Assert.True(File.Exists(Path.Combine(right, "a.txt")));
        Assert.Contains("had changed since", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Undo_SaysSoWhenThereIsNothingToUndo()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Contains("nothing to undo", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Undo_DeletionIsNeverRecorded()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        dir.File("left/a.txt", "x");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, left);
        Select(vm.LeftPanel, "a.txt");
        harness.Dialogs.DeleteAnswer = DeleteChoice.Trash;

        await vm.DeleteCommand.ExecuteAsync(null);
        await vm.UndoCommand.ExecuteAsync(null);

        // The trash has no restore API, so a deletion must not pretend to be undoable.
        Assert.Contains("nothing to undo", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task NewTab_WithASideNamedIgnoresWhichPaneHasTheFocus()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        using var harness = new Harness();
        var vm = harness.ViewModel;
        await GoAsync(vm, left, right);

        // The left pane has the focus, but the right-hand "+" must still open a right-hand tab.
        vm.SetActivePanel(vm.LeftPanel);
        await vm.NewTabCommand.ExecuteAsync("Right");

        Assert.Equal(2, vm.RightTabs.Count);
        Assert.Single(vm.LeftTabs);
        Assert.Equal(PanelSide.Right, vm.CurrentTab!.Side);

        vm.SetActivePanel(vm.RightPanel);
        await vm.NewTabCommand.ExecuteAsync("Left");

        Assert.Equal(2, vm.LeftTabs.Count);
        Assert.Equal(PanelSide.Left, vm.CurrentTab!.Side);
    }

    [AvaloniaFact]
    public async Task NewTab_WithNoSideStillFollowsTheFocus()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        vm.SetActivePanel(vm.RightPanel);

        // This is the keyboard shortcut's path, which passes nothing.
        await vm.NewTabCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.RightTabs.Count);
        Assert.Single(vm.LeftTabs);
    }

    [AvaloniaFact]
    public async Task Startup_AnnouncesTheLanguageEvenWhenTheSettingDidNotChange()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;

        // "System" is both the default and what the settings hold, so the property value does not change. The
        // texts still do, and the views have to re-measure or they keep the widths of the English words.
        harness.Settings.Settings.Language = "System";

        var announced = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentLanguage))
            {
                announced++;
            }
        };

        await vm.InitializeAsync();

        Assert.True(announced > 0, "Starting up must announce the language, or the layout keeps the old widths.");
    }

    // ---- update checks ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(12, false)]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(24 * 30, true)]
    public void DueForCheck_OnceTheIntervalHasPassed(int? hoursAgo, bool expected)
    {
        var last = hoursAgo is { } hours ? DateTime.UtcNow.AddHours(-hours) : (DateTime?)null;

        Assert.Equal(expected, MainWindowViewModel.DueForCheck(last, TimeSpan.FromDays(1)));
    }

    [AvaloniaFact]
    public async Task Startup_ChecksForUpdatesWhenTheSettingIsOnAndTheDayIsUp()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = DateTime.UtcNow.AddDays(-8);

        await harness.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => harness.Updates.Checks > 0, 2000);

        Assert.Equal(1, harness.Updates.Checks);
    }

    [AvaloniaFact]
    public async Task Startup_DoesNotCheckWhenTheSettingIsOff()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = false;
        harness.Settings.Settings.LastUpdateCheck = null;

        await harness.ViewModel.InitializeAsync();
        await Task.Delay(200);

        Assert.Equal(0, harness.Updates.Checks);
    }

    [AvaloniaFact]
    public async Task Startup_DoesNotCheckAgainWithinTheDay()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = DateTime.UtcNow.AddHours(-2);

        await harness.ViewModel.InitializeAsync();
        await Task.Delay(200);

        Assert.Equal(0, harness.Updates.Checks);
    }

    [AvaloniaFact]
    public async Task CheckingByHandOffersToOpenTheDownloadPage()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.Updates.Answer = new UpdateInfo
        {
            Version = new Version(99, 0, 0),
            Tag = "v99.0.0",
            Url = "https://example.invalid/releases/v99.0.0",
            IsNewer = true
        };
        harness.Dialogs.ConfirmAnswer = false;

        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        Assert.Contains("v99.0.0", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(harness.Dialogs.Calls, call => call.StartsWith("confirm", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task CheckingByHandSaysSoWhenAlreadyCurrent()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.Updates.Answer = new UpdateInfo
        {
            Version = new Version(1, 0, 0),
            Tag = "v1.0.0",
            Url = "https://example.invalid",
            IsNewer = false
        };

        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        // Asked for by hand, so the answer has to be on screen, not only in the status bar.
        Assert.Single(harness.Dialogs.Messages);
        Assert.Contains(Strings.Get("Update_CurrentLong").Split('{')[0], harness.Dialogs.Messages[0], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CheckingByHandSaysSoWhenTheServerIsUnreachable()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.Updates.Answer = null;

        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        Assert.Single(harness.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void TogglingTheCheckWritesItToTheSettings()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        Assert.True(vm.CheckForUpdates);

        vm.ToggleUpdateChecksCommand.Execute(null);

        Assert.False(vm.CheckForUpdates);
        Assert.False(harness.Settings.Settings.CheckForUpdates);
    }

    [AvaloniaFact]
    public async Task TheAutomaticCheckStaysQuietWhenThereIsNothingToSay()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = DateTime.UtcNow.AddDays(-8);
        harness.Updates.Answer = null;

        await harness.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => harness.Updates.Checks > 0, 2000);
        await Task.Delay(100);

        // Nobody asked, so a failed check must not interrupt with a dialog.
        Assert.Empty(harness.Dialogs.Messages);
    }

    private static UpdateInfo NewerRelease() => new()
    {
        Version = new Version(99, 1, 0),
        Tag = "v99.1.0",
        Url = "https://github.com/aurgo/sharpCommander/releases/tag/v99.1.0",
        IsNewer = true
    };

    [AvaloniaFact]
    public async Task AReleasePutOffWithLaterStaysOnScreen()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.Updates.Answer = NewerRelease();
        harness.Dialogs.ConfirmAnswer = false;

        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        // The dialog is gone, but the menu bar keeps the reminder.
        Assert.Same(harness.Updates.Answer, vm.AvailableUpdate);
        Assert.Equal("99.1.0", vm.AvailableVersion);
    }

    [AvaloniaFact]
    public async Task TheAutomaticCheckAsksAboutEachReleaseOnlyOnce()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = null;
        harness.Settings.Settings.LastAnnouncedUpdate = "v99.1.0";
        harness.Updates.Answer = NewerRelease();

        await harness.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => harness.ViewModel.AvailableUpdate is not null, 2000);

        // Asked about already: a check a day must not put the same question every morning.
        Assert.DoesNotContain(harness.Dialogs.Calls, call => call.StartsWith("confirm", StringComparison.Ordinal));
        Assert.NotNull(harness.ViewModel.AvailableUpdate);
    }

    [AvaloniaFact]
    public async Task TheAutomaticCheckAsksAboutANewReleaseAndRemembersIt()
    {
        using var harness = new Harness();
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = null;
        harness.Settings.Settings.LastAnnouncedUpdate = "v99.0.0";
        harness.Updates.Answer = NewerRelease();
        harness.Dialogs.ConfirmAnswer = false;

        await harness.ViewModel.InitializeAsync();
        await WaitUntilAsync(() => harness.Dialogs.Calls.Any(call => call.StartsWith("confirm", StringComparison.Ordinal)), 2000);

        Assert.Equal("v99.1.0", harness.Settings.Settings.LastAnnouncedUpdate);
    }

    [AvaloniaFact]
    public async Task BeingUpToDateClearsTheReminder()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.Updates.Answer = NewerRelease();
        harness.Dialogs.ConfirmAnswer = false;
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        harness.Updates.Answer = NewerRelease() with { IsNewer = false };
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);

        Assert.Null(vm.AvailableUpdate);
    }

    [AvaloniaFact]
    public async Task OpeningTheUpdateGoesToItsDownloadPage()
    {
        var fileSystem = new DelegatingFileSystem(FileSystem);
        var dialogs = new FakeDialogService { ConfirmAnswer = true };
        var trash = new FakeTrashService();
        var updates = new FakeUpdateService { Answer = NewerRelease() };
        using var vm = new MainWindowViewModel(fileSystem, new FakeSettingsService(), dialogs, new FakeClipboardService(), new FileOperationsService(fileSystem, dialogs, trash), trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), updates, new FakeSpaceAnalyzerService());

        // Once from the dialog's "Open", once from the reminder in the menu bar.
        await vm.CheckForUpdatesNowCommand.ExecuteAsync(null);
        await vm.OpenUpdatePageCommand.ExecuteAsync(null);

        Assert.Equal([updates.Answer.Url, updates.Answer.Url], fileSystem.Opened);
        Assert.Empty(dialogs.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task AnOpenWindowKeepsCheckingOnceADay()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        vm.UpdateWatchInterval = TimeSpan.FromMilliseconds(50);
        harness.Settings.Settings.CheckForUpdates = true;
        harness.Settings.Settings.LastUpdateCheck = DateTime.UtcNow;

        await vm.InitializeAsync();
        await WaitUntilAsync(() => false, 300);
        Assert.Equal(0, harness.Updates.Checks);

        // A day goes by with the window open.
        harness.Settings.Settings.LastUpdateCheck = DateTime.UtcNow.AddDays(-1).AddMinutes(-1);
        await WaitUntilAsync(() => harness.Updates.Checks > 0, 2000);

        Assert.Equal(1, harness.Updates.Checks);
    }

    // ---- SpaceAnalyzer --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SpaceAnalyzer_TheFirstUseDownloadsItAndOpensItOnTheActiveFolder()
    {
        using var harness = new Harness();
        using var dir = new TempDir();
        var vm = harness.ViewModel;
        await GoAsync(vm, dir.Dir("left"), dir.Dir("right"));
        vm.SetActivePanel(vm.RightPanel);
        harness.SpaceAnalyzer.Latest = FakeSpaceAnalyzerService.Release("1.2.0");

        await vm.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        Assert.Equal(new Version(1, 2, 0), Assert.Single(harness.SpaceAnalyzer.Installs).Version);
        var launch = Assert.Single(harness.SpaceAnalyzer.Launches);
        Assert.Equal(new Version(1, 2, 0), launch.Install.Version);
        Assert.Equal(vm.RightPanel.CurrentPath, launch.Folder);
        Assert.NotNull(harness.Settings.Settings.LastSpaceAnalyzerCheck);
        Assert.Empty(harness.Dialogs.Calls);

        // Progress reports still queued when the download ended must not paint a percentage over the result.
        await WaitUntilAsync(() => false, 150);
        Assert.Contains(vm.RightPanel.CurrentPath, vm.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_AKeptCopyOpensWithoutAskingGitHub()
    {
        using var harness = new Harness();
        using var dir = new TempDir();
        var vm = harness.ViewModel;
        await GoAsync(vm, dir.Dir("left"), dir.Dir("right"));
        vm.SetActivePanel(vm.LeftPanel);
        var kept = FakeSpaceAnalyzerService.Install("1.0.0");
        harness.SpaceAnalyzer.Installed = kept;
        harness.Settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow.AddDays(-1);

        await vm.OpenSpaceAnalyzerCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => false, 100);

        Assert.Equal(0, harness.SpaceAnalyzer.Lookups);
        Assert.Empty(harness.SpaceAnalyzer.Installs);
        var launch = Assert.Single(harness.SpaceAnalyzer.Launches);
        Assert.Same(kept, launch.Install);
        Assert.Equal(vm.LeftPanel.CurrentPath, launch.Folder);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_OnceAWeekANewerReleaseIsFetchedForTheNextTime()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        var kept = FakeSpaceAnalyzerService.Install("1.0.0");
        harness.SpaceAnalyzer.Installed = kept;
        harness.SpaceAnalyzer.Latest = FakeSpaceAnalyzerService.Release("1.1.0");
        harness.Settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow.AddDays(-8);

        await vm.OpenSpaceAnalyzerCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => harness.SpaceAnalyzer.Installs.Count > 0, 2000);

        // The copy already kept is the one that opened; the new one only takes over from the next time on.
        Assert.Same(kept, Assert.Single(harness.SpaceAnalyzer.Launches).Install);
        Assert.Equal(new Version(1, 1, 0), Assert.Single(harness.SpaceAnalyzer.Installs).Version);
        Assert.True(DateTime.UtcNow - harness.Settings.Settings.LastSpaceAnalyzerCheck < TimeSpan.FromMinutes(1));
        Assert.Contains("1.1.0", vm.StatusMessage, StringComparison.Ordinal);

        // Nobody asked for the check, so it never interrupts with a dialog.
        Assert.Empty(harness.Dialogs.Calls);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_TheWeeklyCheckKeepsTheCopyWhenNothingIsNewer()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.Installed = FakeSpaceAnalyzerService.Install("1.1.0");
        harness.SpaceAnalyzer.Latest = FakeSpaceAnalyzerService.Release("1.1.0");
        harness.Settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow.AddDays(-8);

        await harness.ViewModel.OpenSpaceAnalyzerCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => harness.SpaceAnalyzer.Lookups > 0, 2000);

        Assert.Empty(harness.SpaceAnalyzer.Installs);
        Assert.Single(harness.SpaceAnalyzer.Launches);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_AFailedWeeklyCheckStaysQuiet()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.Installed = FakeSpaceAnalyzerService.Install("1.0.0");
        harness.SpaceAnalyzer.Latest = FakeSpaceAnalyzerService.Release("1.1.0");
        harness.SpaceAnalyzer.InstallFailure = new HttpRequestException("no network");
        harness.Settings.Settings.LastSpaceAnalyzerCheck = null;

        await harness.ViewModel.OpenSpaceAnalyzerCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => harness.SpaceAnalyzer.Installs.Count > 0, 2000);

        Assert.Single(harness.SpaceAnalyzer.Launches);
        Assert.Empty(harness.Dialogs.Calls);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_WhenGitHubCannotBeReachedTheFirstTimeItSaysSo()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.Latest = null;

        await harness.ViewModel.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        Assert.Empty(harness.SpaceAnalyzer.Installs);
        Assert.Empty(harness.SpaceAnalyzer.Launches);
        Assert.StartsWith("SpaceAnalyzer|", Assert.Single(harness.Dialogs.Messages), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_AFailedDownloadIsReportedAndNothingOpens()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.Latest = FakeSpaceAnalyzerService.Release("1.2.0");
        harness.SpaceAnalyzer.InstallFailure = new InvalidDataException("does not match the SHA-256");

        await harness.ViewModel.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        Assert.Empty(harness.SpaceAnalyzer.Launches);
        Assert.Contains("does not match the SHA-256", Assert.Single(harness.Dialogs.ErrorMessages), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_ASystemWithoutABuildIsToldSo()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.IsSupported = false;

        await harness.ViewModel.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.SpaceAnalyzer.Lookups);
        Assert.Empty(harness.SpaceAnalyzer.Launches);
        Assert.Single(harness.Dialogs.Messages);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_OnTheComputerViewOpensOnItsStartScreen()
    {
        using var harness = new Harness();
        var vm = harness.ViewModel;
        harness.SpaceAnalyzer.Installed = FakeSpaceAnalyzerService.Install("1.0.0");
        harness.Settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow;
        await vm.LeftPanel.InitializeAsync(string.Empty);
        vm.SetActivePanel(vm.LeftPanel);
        Assert.True(vm.LeftPanel.IsRootView);

        await vm.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        // Its start screen lists the drives, which is what the Computer view shows too.
        Assert.Null(Assert.Single(harness.SpaceAnalyzer.Launches).Folder);
    }

    [AvaloniaFact]
    public async Task SpaceAnalyzer_OnAServerOpensOnItsStartScreen()
    {
        var server = new FakeSftpServer();
        server.AddDirectory("/home/ana");
        var connections = new SftpConnections(() => server);
        await connections.ConnectAsync(new SftpSite { Host = "example.com", Port = 22, Username = "ana" }, null);
        var fileSystem = new RoutingFileSystemService(FileSystem, connections);
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        var settings = new FakeSettingsService();
        settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow;
        var spaceAnalyzer = new FakeSpaceAnalyzerService { Installed = FakeSpaceAnalyzerService.Install("1.0.0") };
        using var vm = new MainWindowViewModel(fileSystem, settings, dialogs, new FakeClipboardService(), new FileOperationsService(fileSystem, dialogs, trash), trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), connections, new FakeUpdateService(), spaceAnalyzer);
        await vm.LeftPanel.NavigateToAsync("sftp://ana@example.com:22/home/ana");
        vm.SetActivePanel(vm.LeftPanel);

        await vm.OpenSpaceAnalyzerCommand.ExecuteAsync(null);

        // A remote folder cannot be scanned from here; the start screen is the honest place to land.
        Assert.Null(Assert.Single(spaceAnalyzer.Launches).Folder);
        Assert.DoesNotContain("sftp://", vm.StatusMessage, StringComparison.Ordinal);
    }
}
