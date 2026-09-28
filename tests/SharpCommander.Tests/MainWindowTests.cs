using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The real main window driven with headless key presses and drops, with fakes for dialogs, settings, clipboard
/// and trash: shortcuts, the tab strip, the status bar, selection sync and the closing sequence.
/// </summary>
public class MainWindowTests
{
    private static readonly FileSystemService FileSystem = new();

    private sealed class Harness : IDisposable
    {
        public TempDir Dir { get; } = new();
        public string Left { get; }
        public string Right { get; }
        public FakeSettingsService Settings { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeClipboardService Clipboard { get; } = new();
        public FakeTrashService Trash { get; } = new();
        public FakeSpaceAnalyzerService SpaceAnalyzer { get; } = new();
        public FakeUpdateService Updates { get; } = new();
        public MainWindowViewModel ViewModel { get; }
        public MainWindow Window { get; }

        public Harness(IFileSystemService? fileSystem = null)
        {
            var fs = fileSystem ?? FileSystem;
            Left = Dir.Dir("left");
            Right = Dir.Dir("right");
            Settings.Settings.LastLeftPanelPath = Left;
            Settings.Settings.LastRightPanelPath = Right;

            var operations = new FileOperationsService(fs, Dialogs, Trash);
            ViewModel = new MainWindowViewModel(fs, Settings, Dialogs, Clipboard, operations, Trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), Updates, SpaceAnalyzer);
            Window = new MainWindow { DataContext = ViewModel };
        }

        public FilePanelViewModel LeftPanel => ViewModel.LeftPanel;

        public FilePanelViewModel RightPanel => ViewModel.RightPanel;

        public FilePanelView LeftView => Window.FindControl<FilePanelView>("LeftPanelView")!;

        public FilePanelView RightView => Window.FindControl<FilePanelView>("RightPanelView")!;

        public ListBox LeftList => LeftView.FindControl<ListBox>("FileListBox")!;

        public ListBox RightList => RightView.FindControl<ListBox>("FileListBox")!;

        /// <summary>Shows the window and waits for the view model initialization the window starts on Loaded.</summary>
        public async Task ShowAsync()
        {
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            await Window.Initialization;
            await PumpAsync();
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            Dir.Dispose();
        }
    }

    private static async Task PumpAsync(int iterations = 5)
    {
        for (var i = 0; i < iterations; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await PumpAsync(1);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        if (window.IsVisible)
        {
            // F10 closes the window synchronously; a release on a closed headless window throws.
            window.KeyReleaseQwerty(key, modifiers);
        }
    }

    /// <summary>The tab chips rendered in one of the two groups of the tab bar, in order.</summary>
    private static List<Border> TabsOf(Window window, string groupName)
    {
        window.UpdateLayout();
        return window.FindControl<ItemsControl>(groupName)!
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("tab"))
            .ToList();
    }

    /// <summary>Clicks the middle of a laid-out control with the headless mouse.</summary>
    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    // ---- C3: Delete key ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DeleteKey_AsksAndHonoursTheAnswer()
    {
        using var harness = new Harness();
        var file = harness.Dir.File(Path.Combine("left", "a.txt"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        Assert.True(harness.LeftPanel.SelectPath(file));

        harness.Dialogs.DeleteAnswer = DeleteChoice.Cancel;
        Press(harness.Window, PhysicalKey.Delete);
        await PumpAsync();

        Assert.True(File.Exists(file));
        Assert.Equal(1, harness.Dialogs.Calls.Count(call => call == "delete:1:trash:default"));

        harness.Dialogs.DeleteAnswer = DeleteChoice.Trash;
        Press(harness.Window, PhysicalKey.Delete);
        await WaitUntilAsync(() => harness.Trash.Trashed.Count == 1);

        Assert.Equal([file], harness.Trash.Trashed);
        Assert.False(File.Exists(file));
        Assert.DoesNotContain(harness.LeftPanel.FilteredEntries, entry => entry.Name == "a.txt");
    }

    [AvaloniaFact]
    public async Task F8_AndShiftDelete_AskWithTheRightDefault()
    {
        using var harness = new Harness();
        var file = harness.Dir.File(Path.Combine("left", "a.txt"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        Assert.True(harness.LeftPanel.SelectPath(file));

        harness.Dialogs.DeleteAnswer = DeleteChoice.Cancel;
        Press(harness.Window, PhysicalKey.F8);
        await PumpAsync();
        Assert.Equal(["delete:1:trash:default"], harness.Dialogs.Calls.Where(call => call.StartsWith("delete:", StringComparison.Ordinal)));
        Assert.True(File.Exists(file));

        harness.Dialogs.DeleteAnswer = DeleteChoice.Permanent;
        Press(harness.Window, PhysicalKey.Delete, RawInputModifiers.Shift);
        await WaitUntilAsync(() => !File.Exists(file));

        Assert.Contains("delete:1:trash:permanent", harness.Dialogs.Calls);
        Assert.Empty(harness.Trash.Trashed);
        Assert.False(File.Exists(file));
        await WaitUntilAsync(() => harness.ViewModel.StatusMessage.Contains("Deleted 1 item", StringComparison.Ordinal));
        Assert.Contains("Deleted 1 item", harness.ViewModel.StatusMessage);
    }

    // ---- F5 / F6 --------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task F5_CopiesTheSelectionToTheOtherPanel()
    {
        using var harness = new Harness();
        var file = harness.Dir.File(Path.Combine("left", "a.txt"), "copied");
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        harness.LeftPanel.SelectPath(file);

        Press(harness.Window, PhysicalKey.F5);
        await WaitUntilAsync(() => File.Exists(Path.Combine(harness.Right, "a.txt")));

        Assert.Equal("copied", File.ReadAllText(Path.Combine(harness.Right, "a.txt")));
        Assert.True(File.Exists(file));
        await WaitUntilAsync(() => harness.RightPanel.FilteredEntries.Any(entry => entry.Name == "a.txt"));
        Assert.Contains("Copied 1 item", harness.ViewModel.StatusMessage);
        Assert.Empty(harness.Dialogs.ReportedErrors);
    }

    [AvaloniaFact]
    public async Task F6_WithBothPanelsOnTheSameFolder_DeletesNothing()
    {
        using var harness = new Harness();
        var file = harness.Dir.File(Path.Combine("left", "a.txt"), "safe");
        await harness.ShowAsync();
        await harness.RightPanel.NavigateToCommand.ExecuteAsync(harness.Left);
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        harness.LeftPanel.SelectPath(file);

        Press(harness.Window, PhysicalKey.F6);
        await WaitUntilAsync(() => harness.Dialogs.ReportedErrors.Count > 0);

        Assert.Equal("safe", File.ReadAllText(file));
        Assert.Single(harness.Dialogs.ReportedErrors);
        Assert.Contains(harness.LeftPanel.FilteredEntries, entry => entry.Name == "a.txt");
    }

    // ---- H1: status bar -------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task StatusBar_ShowsTheLastMessageAndHidesProgressWhenIdle()
    {
        using var harness = new Harness();
        await harness.ShowAsync();

        var text = harness.Window.FindControl<TextBlock>("StatusMessageText")!;
        var operations = harness.Window.FindControl<StackPanel>("OperationPanel")!;
        Assert.Contains("Ready.", harness.ViewModel.StatusMessage);
        Assert.Equal(harness.ViewModel.StatusMessage, text.Text);
        Assert.False(operations.IsVisible);
        Assert.False(harness.Window.FindControl<Button>("CancelOperationButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task StatusBar_ShowsARunningOperationAndCancelsIt()
    {
        var gate = new TaskCompletionSource();
        var fileSystem = new DelegatingFileSystem(FileSystem)
        {
            BeforeTransfer = (_, token) => gate.Task.WaitAsync(token)
        };
        using var harness = new Harness(fileSystem);
        var file = harness.Dir.File(Path.Combine("left", "big.bin"), new string('x', 4096));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        Assert.True(harness.LeftPanel.SelectPath(file));

        Press(harness.Window, PhysicalKey.F5);
        await WaitUntilAsync(() => harness.ViewModel.Operations.IsRunning);
        await PumpAsync();

        var operations = harness.Window.FindControl<StackPanel>("OperationPanel")!;
        var cancel = harness.Window.FindControl<Button>("CancelOperationButton")!;
        var progress = harness.Window.FindControl<ProgressBar>("OperationProgress")!;
        Assert.True(operations.IsVisible);
        Assert.Equal("Copying", harness.ViewModel.Operations.OperationName);
        Assert.Equal(file, harness.ViewModel.Operations.CurrentFile);
        Assert.Equal(4096, harness.ViewModel.Operations.TotalBytes);
        Assert.Equal(1, harness.ViewModel.Operations.TotalItems);
        Assert.Equal(100, progress.Maximum);
        Assert.True(cancel.IsEffectivelyEnabled);

        Click(harness.Window, cancel);
        await WaitUntilAsync(() => !harness.ViewModel.Operations.IsRunning);
        await PumpAsync();

        Assert.False(File.Exists(Path.Combine(harness.Right, "big.bin")));
        Assert.True(File.Exists(file));
        Assert.False(operations.IsVisible);
        Assert.False(cancel.IsEffectivelyEnabled);
        Assert.Contains("Cancelled", harness.ViewModel.StatusMessage);
        gate.TrySetResult();
    }

    // ---- H6: tabs -------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CtrlTab_CyclesTheTabs()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        Press(harness.Window, PhysicalKey.T, RawInputModifiers.Control);
        await WaitUntilAsync(() => harness.ViewModel.LeftTabs.Count == 2);
        Assert.Same(harness.ViewModel.LeftTabs[1], harness.ViewModel.CurrentTab);

        Press(harness.Window, PhysicalKey.Tab, RawInputModifiers.Control);
        await PumpAsync();
        Assert.Same(harness.ViewModel.LeftTabs[0], harness.ViewModel.CurrentTab);

        Press(harness.Window, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        await PumpAsync();
        Assert.Same(harness.ViewModel.LeftTabs[1], harness.ViewModel.CurrentTab);
        var current = TabsOf(harness.Window, "LeftTabStrip").Single(tab => tab.Classes.Contains("current"));
        Assert.Same(harness.ViewModel.CurrentTab, current.DataContext);
    }

    [AvaloniaFact]
    public async Task CtrlT_AddsATabThatTheStripShows()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        var strip = harness.Window.FindControl<ItemsControl>("LeftTabStrip")!;

        // Each pane starts with a tab of its own, one to a group.
        Assert.Single(TabsOf(harness.Window, "LeftTabStrip"));
        Assert.Single(TabsOf(harness.Window, "RightTabStrip"));

        var closeButtons = () => strip.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tab-close")).ToList();
        Assert.All(closeButtons(), button => Assert.False(button.IsVisible));

        Press(harness.Window, PhysicalKey.T, RawInputModifiers.Control);
        await WaitUntilAsync(() => harness.ViewModel.LeftTabs.Count == 2);

        // The left pane is the active one, so the new tab joins the left group and the right one is untouched.
        var chips = TabsOf(harness.Window, "LeftTabStrip");
        Assert.Equal(2, chips.Count);
        Assert.Single(TabsOf(harness.Window, "RightTabStrip"));
        Assert.Same(harness.ViewModel.LeftTabs[1], harness.ViewModel.CurrentTab);
        Assert.Same(harness.ViewModel.CurrentTab, chips.Single(tab => tab.Classes.Contains("current")).DataContext);
        Assert.Equal(harness.Left, harness.ViewModel.LeftPanel.CurrentPath);
        Assert.Equal(2, closeButtons().Count);
        Assert.All(closeButtons(), button => Assert.True(button.IsVisible));

        Click(harness.Window, chips[0]);
        await PumpAsync();
        Assert.Same(harness.ViewModel.LeftTabs[0], harness.ViewModel.CurrentTab);
    }

    [AvaloniaFact]
    public async Task CtrlW_RefusesToCloseTheLastTabOfAPane()
    {
        using var harness = new Harness();
        await harness.ShowAsync();

        Press(harness.Window, PhysicalKey.W, RawInputModifiers.Control);
        await PumpAsync();

        Assert.Single(harness.ViewModel.LeftTabs);
        Assert.Contains("last tab", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        Press(harness.Window, PhysicalKey.T, RawInputModifiers.Control);
        await WaitUntilAsync(() => harness.ViewModel.LeftTabs.Count == 2);
        Press(harness.Window, PhysicalKey.W, RawInputModifiers.Control);
        await WaitUntilAsync(() => harness.ViewModel.LeftTabs.Count == 1);

        Assert.Single(TabsOf(harness.Window, "LeftTabStrip"));
    }

    // ---- H5: column headers -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ColumnHeaderClick_SortsTheListingAndPersistsIt()
    {
        using var harness = new Harness();
        harness.Dir.File(Path.Combine("left", "a.txt"), "xxx");
        harness.Dir.File(Path.Combine("left", "b.txt"), "x");
        await harness.ShowAsync();
        var header = harness.LeftView.FindControl<Button>("SizeHeader")!;

        Click(harness.Window, header);
        await PumpAsync();

        Assert.Equal("Size", harness.LeftPanel.SortColumn);
        Assert.False(harness.LeftPanel.SortDescending);
        Assert.Equal(new[] { "..", "b.txt", "a.txt" }, harness.LeftPanel.FilteredEntries.Select(entry => entry.Name));
        Assert.Equal("Size", harness.Settings.Settings.SortColumn);

        Click(harness.Window, header);
        await PumpAsync();

        Assert.True(harness.LeftPanel.SortDescending);
        Assert.Equal(new[] { "..", "a.txt", "b.txt" }, harness.LeftPanel.FilteredEntries.Select(entry => entry.Name));
        Assert.Equal("Descending", harness.Settings.Settings.SortDirection);
        Assert.True(harness.Settings.RequestSaveCount >= 2);
    }

    // ---- C4: selection sync -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CtrlA_SelectsAllRowsInTheListBox()
    {
        using var harness = new Harness();
        for (var i = 0; i < 3; i++)
        {
            harness.Dir.File(Path.Combine("left", $"f{i}.txt"));
        }

        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);

        Press(harness.Window, PhysicalKey.A, RawInputModifiers.Control);
        await PumpAsync();

        Assert.Equal(3, harness.LeftPanel.SelectedEntries.Count);
        Assert.Equal(3, harness.LeftList.Selection.Count);
        Assert.DoesNotContain(harness.LeftList.SelectedItems!.OfType<FileSystemEntry>(), entry => entry.EntryType == FileSystemEntryType.ParentDirectory);
        Assert.Contains("3 selected", harness.LeftPanel.StatusText);
    }

    [AvaloniaFact]
    public async Task ListSelection_FlowsBackToTheViewModel()
    {
        using var harness = new Harness();
        harness.Dir.File(Path.Combine("left", "a.txt"));
        harness.Dir.File(Path.Combine("left", "b.txt"));
        await harness.ShowAsync();
        var list = harness.LeftList;

        list.Selection.BeginBatchUpdate();
        list.Selection.Clear();
        list.Selection.Select(1);
        list.Selection.Select(2);
        list.Selection.EndBatchUpdate();
        await PumpAsync();

        Assert.Equal(["a.txt", "b.txt"], harness.LeftPanel.SelectedEntries.Select(entry => entry.Name));
        Assert.Contains("2 selected", harness.LeftPanel.StatusText);

        list.SelectedIndex = 0; // ".." can be the cursor, but never part of a multi-selection
        await PumpAsync();
        Assert.Equal(FileSystemEntryType.ParentDirectory, harness.LeftPanel.SelectedEntry?.EntryType);
    }

    /// <summary>
    /// Both panels keep a selected row at all times, so the colour of the selection is what says where F5, F6
    /// and the cursor keys will act: only the active list carries the class the accent style is written for,
    /// and the other one falls to the grey one.
    /// </summary>
    [AvaloniaFact]
    public async Task OnlyTheActivePanel_MarksItsListAndHeaderAsActive()
    {
        using var harness = new Harness();
        await harness.ShowAsync();

        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        await PumpAsync();

        Assert.Contains("active", harness.LeftList.Classes);
        Assert.Contains("active", harness.LeftView.FindControl<Border>("PanelHeader")!.Classes);
        Assert.DoesNotContain("active", harness.RightList.Classes);

        harness.ViewModel.SetActivePanel(harness.RightPanel);
        await PumpAsync();

        Assert.DoesNotContain("active", harness.LeftList.Classes);
        Assert.Contains("active", harness.RightList.Classes);
        Assert.Contains("active", harness.RightView.FindControl<Border>("PanelHeader")!.Classes);
    }

    // ---- L8 / D5: favorites and shortcuts ----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CtrlD_TogglesTheFavoriteOfTheActivePanel()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        var favorites = harness.Window.FindControl<ListBox>("FavoritesListBox")!;

        Press(harness.Window, PhysicalKey.D, RawInputModifiers.Control);
        await WaitUntilAsync(() => harness.LeftPanel.IsFavorite);

        Assert.Contains(harness.Settings.Settings.Favorites, favorite => favorite.Path == harness.Left);
        Assert.Equal(1, favorites.ItemCount);
        Assert.Contains(harness.RightPanel.Favorites, favorite => favorite.Path == harness.Left);

        Press(harness.Window, PhysicalKey.D, RawInputModifiers.Control);
        await WaitUntilAsync(() => !harness.LeftPanel.IsFavorite);

        Assert.Empty(harness.Settings.Settings.Favorites);
        Assert.Equal(0, favorites.ItemCount);
    }

    [AvaloniaFact]
    public async Task MoveFavorite_ReordersAndPersists()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        var first = harness.Dir.Dir("one");
        var second = harness.Dir.Dir("two");
        await harness.Settings.AddFavoriteAsync(first);
        await harness.Settings.AddFavoriteAsync(second);
        harness.LeftPanel.LoadFavorites();
        var favorite = harness.Settings.Settings.Favorites.Single(item => item.Path == second);

        await harness.ViewModel.MoveFavoriteAsync(favorite, 0);

        Assert.Equal([second, first], harness.LeftPanel.Favorites.Select(item => item.Path));
        Assert.Equal(0, favorite.Order);
        Assert.True(harness.Settings.RequestSaveCount >= 1);
    }

    [AvaloniaFact]
    public async Task CtrlF_TogglesTheFilterBox()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);

        Press(harness.Window, PhysicalKey.F, RawInputModifiers.Control);
        await PumpAsync();
        Assert.True(harness.LeftPanel.IsSearchActive);

        Press(harness.Window, PhysicalKey.F, RawInputModifiers.Control);
        await PumpAsync();
        Assert.False(harness.LeftPanel.IsSearchActive);
    }

    [AvaloniaFact]
    public async Task TypeAhead_SelectsByTypedText()
    {
        using var harness = new Harness();
        harness.Dir.File(Path.Combine("left", "apple.txt"));
        harness.Dir.File(Path.Combine("left", "banana.txt"));
        await harness.ShowAsync();
        harness.LeftPanel.RequestFocus();
        await PumpAsync();
        Assert.True(harness.LeftList.IsKeyboardFocusWithin);

        harness.Window.KeyTextInput("b");
        await PumpAsync();

        Assert.Equal("banana.txt", harness.LeftPanel.SelectedEntry?.Name);
        Assert.Contains(harness.LeftList.SelectedItems!.OfType<FileSystemEntry>(), entry => entry.Name == "banana.txt");
    }

    // ---- M9: drop -----------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Drop_FromTheOtherPanel_MovesTheFiles()
    {
        using var harness = new Harness();
        var file = harness.Dir.File(Path.Combine("right", "dropped.txt"), "d");
        await harness.ShowAsync();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(FilePanelView.InternalPathsFormat, file));
        var point = harness.LeftList.TranslatePoint(new Point(20, 20), harness.Window)!.Value;
        var window = harness.Window;
        window.DragDrop(point, RawDragEventType.DragEnter, transfer, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
        window.DragDrop(point, RawDragEventType.DragOver, transfer, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
        Assert.Contains("drop-target", harness.LeftList.Classes);
        window.DragDrop(point, RawDragEventType.Drop, transfer, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
        await WaitUntilAsync(() => File.Exists(Path.Combine(harness.Left, "dropped.txt")));

        Assert.False(File.Exists(file));
        Assert.DoesNotContain("drop-target", harness.LeftList.Classes);
        await WaitUntilAsync(() => harness.LeftPanel.FilteredEntries.Any(entry => entry.Name == "dropped.txt"));
        Assert.Contains("Moved 1 item", harness.ViewModel.StatusMessage);
    }

    // ---- M11: closing -----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Closing_WaitsForTheSettingsAndDisposesTheViewModel()
    {
        var harness = new Harness();
        await harness.ShowAsync();

        harness.Window.Close();
        await WaitUntilAsync(() => !harness.Window.IsVisible);

        Assert.Equal(1, harness.Settings.SaveCount);
        Assert.True(harness.Settings.FlushCount >= 1); // ShutdownAsync flushes; Dispose flushes again, best effort
        Assert.Equal(harness.Left, harness.Settings.Settings.LastLeftPanelPath);
        Assert.Equal("Normal", harness.Settings.Settings.WindowState);
        Assert.Empty(harness.ViewModel.Tabs);
        harness.Dispose();
    }

    [AvaloniaFact]
    public async Task F10_ExitsThroughTheViewModel()
    {
        var harness = new Harness();
        await harness.ShowAsync();

        Press(harness.Window, PhysicalKey.F10);
        await WaitUntilAsync(() => !harness.Window.IsVisible);

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Settings.SaveCount);
        harness.Dispose();
    }

    // ---- audit regressions: keyboard focus survives a navigation -----------------------------------------

    [AvaloniaFact]
    public async Task Enter_OnAFolder_KeepsKeyboardFocusSoTheCursorKeysStillWork()
    {
        // A navigation replaces every row, so the ListBox recycles the focused one and Avalonia clears
        // keyboard focus to null. The reveal callback tested the focus it had already lost, so after Enter the
        // arrows, Enter, Backspace and type-ahead were all dead until the user clicked.
        using var harness = new Harness();
        var sub = harness.Dir.Dir(Path.Combine("left", "sub"));
        harness.Dir.File(Path.Combine("left", "sub", "a.txt"));
        harness.Dir.File(Path.Combine("left", "sub", "b.txt"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        Assert.True(harness.LeftPanel.SelectPath(sub));
        harness.LeftPanel.RequestFocus();
        await PumpAsync();
        Assert.True(harness.LeftList.IsKeyboardFocusWithin);

        Press(harness.Window, PhysicalKey.Enter);
        await WaitUntilAsync(() => PathUtils.AreSamePath(harness.LeftPanel.CurrentPath, sub));

        Assert.True(harness.LeftList.IsKeyboardFocusWithin);

        // The cursor keys act on the list, not on the menu bar.
        var before = harness.LeftPanel.SelectedEntry?.Name;
        Press(harness.Window, PhysicalKey.ArrowDown);
        await PumpAsync();
        Assert.NotEqual(before, harness.LeftPanel.SelectedEntry?.Name);

        // And Backspace navigates back up, landing on the folder we came from.
        Press(harness.Window, PhysicalKey.Backspace);
        await WaitUntilAsync(() => PathUtils.AreSamePath(harness.LeftPanel.CurrentPath, harness.Left));
        Assert.True(harness.LeftList.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task Navigation_DoesNotStealFocusFromThePathBox()
    {
        // Restoring the cursor row must not fight the user who is typing a path.
        using var harness = new Harness();
        harness.Dir.Dir(Path.Combine("left", "sub"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);

        var pathBox = harness.LeftView.FindControl<AutoCompleteBox>("PathBox")!;
        pathBox.Focus();
        await PumpAsync();
        Assert.True(pathBox.IsKeyboardFocusWithin);

        await harness.LeftPanel.RefreshAsync();
        await PumpAsync();

        Assert.False(harness.LeftList.IsKeyboardFocusWithin);
    }

    // ---- audit regression: favorites reached with the mouse ----------------------------------------------

    [AvaloniaFact]
    public async Task Delete_OnAFavoriteClickedWithTheMouse_RemovesTheFavoriteAndNotTheFiles()
    {
        // Clicking a favorite navigates and then clears the selection, but keyboard focus stays on the row.
        // Resolving the target from SelectedItem alone missed it, so Delete fell through to the window and ran
        // the file delete command on the panel instead.
        using var harness = new Harness();
        harness.Dir.File(Path.Combine("left", "keep.txt"));
        await harness.ShowAsync();

        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        await harness.ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);
        await PumpAsync();
        var favorites = harness.Window.FindControl<ListBox>("FavoritesListBox")!;
        var custom = harness.LeftPanel.Favorites.FirstOrDefault(favorite => !favorite.IsSystem);
        Assert.NotNull(custom);

        harness.Window.UpdateLayout();
        var row = favorites.GetRealizedContainers().OfType<ListBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, custom));
        Assert.NotNull(row);

        Click(harness.Window, row!);
        await PumpAsync();

        var beforeDialogs = harness.Dialogs.Calls.Count(call => call.StartsWith("delete:", StringComparison.Ordinal));
        Press(harness.Window, PhysicalKey.Delete);
        await PumpAsync();

        Assert.DoesNotContain(harness.LeftPanel.Favorites, favorite => ReferenceEquals(favorite, custom));
        Assert.Equal(beforeDialogs, harness.Dialogs.Calls.Count(call => call.StartsWith("delete:", StringComparison.Ordinal)));
    }

    // ---- audit regressions: the path box ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PathBox_FormatsHistoryItemsAsTheirPath()
    {
        // The suggestion list filters and completes on the item's text. Formatting it as the type name made
        // every real path fragment match nothing, and picking an entry navigated to
        // "SharpCommander.Core.Models.NavigationHistoryItem".
        using var harness = new Harness();
        var sub = harness.Dir.Dir(Path.Combine("left", "reports"));
        await harness.ShowAsync();

        await harness.LeftPanel.NavigateToCommand.ExecuteAsync(sub);
        await PumpAsync();

        var item = harness.LeftPanel.NavigationHistory.FirstOrDefault(entry => PathUtils.AreSamePath(entry.Path, sub));
        Assert.NotNull(item);
        Assert.Equal(item!.Path, item.ToString());

        var pathBox = harness.LeftView.FindControl<AutoCompleteBox>("PathBox")!;
        Assert.NotNull(pathBox.ValueMemberBinding);
    }

    [AvaloniaFact]
    public async Task PathBox_HasNoEnterKeyBindingAndNavigatesFromTheCodeBehind()
    {
        // Enter used to be a KeyBinding on the AutoCompleteBox. Avalonia evaluates key bindings before the
        // control sees the key, so the box could never commit a highlighted suggestion: the first Enter always
        // navigated to whatever raw text was in the box. The binding is gone and the code-behind handler
        // navigates only while the drop-down is closed.
        using var harness = new Harness();
        var sub = harness.Dir.Dir(Path.Combine("left", "reports"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);

        var pathBox = harness.LeftView.FindControl<AutoCompleteBox>("PathBox")!;
        Assert.DoesNotContain(pathBox.KeyBindings, binding => binding.Gesture?.Key == Key.Enter);

        pathBox.Focus();
        await PumpAsync();
        Assert.False(pathBox.IsDropDownOpen);

        harness.LeftPanel.EditablePath = sub;
        await PumpAsync();
        Press(harness.Window, PhysicalKey.Enter);
        await WaitUntilAsync(() => PathUtils.AreSamePath(harness.LeftPanel.CurrentPath, sub));

        Assert.True(PathUtils.AreSamePath(harness.LeftPanel.CurrentPath, sub));
    }

    [AvaloniaFact]
    public async Task PathBox_F4_ReachesTheEditCommandInsteadOfTheDropDown()
    {
        // AutoCompleteBox handles F4 as a drop-down toggle and marks it handled, so the Edit shortcut the menu,
        // the toolbar and the function bar all advertise was dead while the caret was in the path box. Nothing
        // is selected here, so the command takes its "nothing to edit" branch and no editor is launched.
        using var harness = new Harness();
        harness.Dir.Dir(Path.Combine("left", "sub"));
        await harness.ShowAsync();
        harness.ViewModel.SetActivePanel(harness.LeftPanel);
        harness.LeftPanel.SelectedEntry = null;

        var pathBox = harness.LeftView.FindControl<AutoCompleteBox>("PathBox")!;
        pathBox.Focus();
        await PumpAsync();

        harness.ViewModel.StatusMessage = string.Empty;
        Press(harness.Window, PhysicalKey.F4);
        await PumpAsync();

        Assert.Contains("edit", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- language ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SwitchingLanguage_RelaysOutTheHeadersInsteadOfClippingThem()
    {
        using var harness = new Harness();
        await harness.ShowAsync();

        try
        {
            harness.ViewModel.SetLanguageCommand.Execute("es");
            await PumpAsync();
            harness.Window.UpdateLayout();

            var header = harness.Window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "NameHeader");
            var text = header.GetVisualDescendants().OfType<TextBlock>().First();

            Assert.Equal("Nombre", text.Text);

            // A translated word is wider than the original; without a fresh layout it would be drawn clipped.
            Assert.Equal(text.DesiredSize.Width, text.Bounds.Width, 1);
        }
        finally
        {
            SharpCommander.Desktop.Localization.Strings.Use("en");
        }
    }

    // ---- ribbon ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheSpaceAnalyzerButton_OpensItOnTheActivePanesFolder()
    {
        using var harness = new Harness();
        harness.SpaceAnalyzer.Installed = FakeSpaceAnalyzerService.Install("1.0.0");
        harness.Settings.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow;
        await harness.ShowAsync();

        Click(harness.Window, harness.Window.FindControl<Button>("SpaceAnalyzerButton")!);
        await WaitUntilAsync(() => harness.SpaceAnalyzer.Launches.Count > 0);

        Assert.Equal(harness.LeftPanel.CurrentPath, Assert.Single(harness.SpaceAnalyzer.Launches).Folder);
    }

    /// <summary>
    /// Every command of the ribbon is visible at the smallest size the window allows, in every language: the
    /// labels change width with the language, and a button cut off at the edge is one nobody can find.
    /// </summary>
    [AvaloniaFact]
    public async Task TheRibbon_FitsTheSmallestWindowInEveryLanguage()
    {
        using var harness = new Harness();
        harness.Window.Width = harness.Window.MinWidth;
        await harness.ShowAsync();

        try
        {
            foreach (var language in new[] { "en", "es" })
            {
                harness.ViewModel.SetLanguageCommand.Execute(language);
                await PumpAsync();
                harness.Window.UpdateLayout();

                var ribbon = harness.Window.FindControl<StackPanel>("Ribbon")!;
                var buttons = ribbon.GetVisualDescendants().OfType<Button>().ToList();
                Assert.Equal(10, buttons.Count);

                foreach (var button in buttons)
                {
                    var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), ribbon)!.Value.X;
                    Assert.True(right <= ribbon.Bounds.Width,
                        $"In '{language}' a ribbon button ends at {right:0}, past the {ribbon.Bounds.Width:0} of the ribbon.");
                }
            }
        }
        finally
        {
            SharpCommander.Desktop.Localization.Strings.Use("en");
        }
    }

    [AvaloniaFact]
    public async Task TheFavoritesButton_ShowsWhetherThePanelIsOpen()
    {
        using var harness = new Harness();
        await harness.ShowAsync();
        var button = harness.Window.FindControl<StackPanel>("Ribbon")!
            .GetVisualDescendants().OfType<Button>()
            .Single(candidate => candidate.Command == harness.ViewModel.ToggleFavoritesPanelCommand);
        Assert.True(harness.ViewModel.ShowFavoritesPanel);
        Assert.Contains("checked", button.Classes);

        Click(harness.Window, button);
        await PumpAsync();

        Assert.False(harness.ViewModel.ShowFavoritesPanel);
        Assert.DoesNotContain("checked", button.Classes);
    }

    [AvaloniaFact]
    public async Task TheNewVersionButton_AppearsOnlyWithANewerReleaseAndOpensItsPage()
    {
        var fileSystem = new DelegatingFileSystem(FileSystem);
        using var harness = new Harness(fileSystem);
        harness.Settings.Settings.CheckForUpdates = false;
        await harness.ShowAsync();
        var button = harness.Window.FindControl<Button>("UpdateButton")!;
        Assert.False(button.IsVisible);

        harness.Updates.Answer = new UpdateInfo
        {
            Version = new Version(99, 1, 0),
            Tag = "v99.1.0",
            Url = "https://github.com/aurgo/sharpCommander/releases/tag/v99.1.0",
            IsNewer = true
        };
        harness.Dialogs.ConfirmAnswer = false;
        await harness.ViewModel.CheckForUpdatesNowCommand.ExecuteAsync(null);
        await PumpAsync();

        Assert.True(button.IsEffectivelyVisible);
        Assert.Contains("99.1.0", string.Concat(button.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Inlines?.Text ?? text.Text)), StringComparison.Ordinal);

        Click(harness.Window, button);
        await WaitUntilAsync(() => fileSystem.Opened.Count > 0);

        Assert.Equal(["https://github.com/aurgo/sharpCommander/releases/tag/v99.1.0"], fileSystem.Opened);
    }
}
