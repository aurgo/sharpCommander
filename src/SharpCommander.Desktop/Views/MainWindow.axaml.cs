using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Utilities;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Main application window: dispatches the keyboard shortcuts, hosts the tab strip, the favorites panel (click
/// to navigate, drag to reorder) and the status bar, restores the saved window size and waits for the settings
/// to be written before it closes.
/// </summary>
public partial class MainWindow : Window
{
    private const double ReorderThreshold = 8;

    /// <summary>How long an unattended shutdown waits for a running batch to stop and clean up.</summary>
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The shortcuts, in evaluation order. They are dispatched from <see cref="OnKeyDown"/> so a focused text
    /// box keeps its own keys (Delete, Backspace, Ctrl+A/C/V/X): Avalonia's KeyBindings run before the focused
    /// control sees the key, which is not what a file manager wants while the user edits a path.
    /// The command-modifier entries come from <see cref="Utilities.Shortcuts"/>, so they are Cmd on macOS and
    /// Ctrl elsewhere; <see cref="BuildShortcutMap"/> adds the Ctrl spelling as an alias where the two differ.
    /// Function keys are the same on every platform.
    /// </summary>
    private static readonly (KeyGesture Gesture, Func<MainWindowViewModel, ICommand?> Command)[] ShortcutMap =
        BuildShortcutMap();

    private static (KeyGesture Gesture, Func<MainWindowViewModel, ICommand?> Command)[] BuildShortcutMap()
    {
        (KeyGesture Gesture, Func<MainWindowViewModel, ICommand?> Command)[] entries =
        [
            (KeyGesture.Parse("F2"), vm => vm.RenameCommand),
            (KeyGesture.Parse("F3"), vm => vm.ViewCommand),
            (KeyGesture.Parse("F4"), vm => vm.EditCommand),
            (KeyGesture.Parse("F5"), vm => vm.CopyCommand),
            (KeyGesture.Parse("F6"), vm => vm.MoveCommand),
            (KeyGesture.Parse("F7"), vm => vm.NewFolderCommand),
            (KeyGesture.Parse("F8"), vm => vm.DeleteCommand),
            (KeyGesture.Parse("Delete"), vm => vm.DeleteCommand),
            (KeyGesture.Parse("Shift+Delete"), vm => vm.DeletePermanentCommand),
            (KeyGesture.Parse("F9"), vm => vm.SwapPanelsCommand),
            (KeyGesture.Parse("F10"), vm => vm.ExitCommand),
            (Utilities.Shortcuts.Undo, vm => vm.UndoCommand),
            (Utilities.Shortcuts.SelectAll, vm => vm.SelectAllCommand),
            (Utilities.Shortcuts.Copy, vm => vm.CopyToClipboardCommand),
            (Utilities.Shortcuts.Cut, vm => vm.CutToClipboardCommand),
            (Utilities.Shortcuts.Paste, vm => vm.PasteFromClipboardCommand),
            (Utilities.Shortcuts.Refresh, vm => vm.RefreshCommand),
            (Utilities.Shortcuts.ToggleFavoritesPanel, vm => vm.ToggleFavoritesPanelCommand),
            (Utilities.Shortcuts.ToggleFavorite, vm => vm.ToggleFavoriteCommand),
            (Utilities.Shortcuts.Filter, vm => vm.ToggleSearchCommand),
            (Utilities.Shortcuts.ShowHiddenFiles, vm => vm.ActivePanel?.ToggleShowHiddenFilesCommand),
            (Utilities.Shortcuts.AdvancedSearch, vm => vm.ShowAdvancedSearchCommand),
            (Utilities.Shortcuts.MassRename, vm => vm.ShowMassRenameCommand),
            (Utilities.Shortcuts.Checksums, vm => vm.CalculateHashCommand),
            (Utilities.Shortcuts.NewTab, vm => vm.NewTabCommand),
            (Utilities.Shortcuts.CloseTab, vm => vm.CloseCurrentTabCommand),
            (Utilities.Shortcuts.NextTab, vm => vm.NextTabCommand),
            (Utilities.Shortcuts.PreviousTab, vm => vm.PreviousTabCommand),
            (Utilities.Shortcuts.DuplicateTab, vm => vm.DuplicateTabCommand),
            (Utilities.Shortcuts.CopyPath, vm => vm.ActivePanel?.CopyPathCommand),
            (Utilities.Shortcuts.OpenTerminal, vm => vm.ActivePanel?.OpenTerminalCommand),
            (Utilities.Shortcuts.SelectByPattern, vm => vm.ActivePanel?.SelectByPatternCommand),
            (Utilities.Shortcuts.UnselectByPattern, vm => vm.ActivePanel?.UnselectByPatternCommand),
            (Utilities.Shortcuts.InvertSelection, vm => vm.ActivePanel?.InvertSelectionCommand),
            (Utilities.Shortcuts.FolderSize, vm => vm.ActivePanel?.CalculateFolderSizeCommand)
        ];

        // Ctrl keeps working on macOS, where the primary spelling is Cmd. The more specific gesture is already
        // in the list, so appending the aliases cannot shadow it.
        var aliases = entries
            .Where(entry => entry.Gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            .Select(entry => (Gesture: Utilities.Shortcuts.WithControl(entry.Gesture), entry.Command));

        return [.. entries, .. aliases];
    }

    private MainWindowViewModel? _viewModel;
    private bool _initialized;
    private bool _shuttingDown;
    private bool _shutdownCompleted;
    private FavoriteItem? _draggedFavorite;
    private Point? _favoritePressPoint;
    private bool _reorderingFavorites;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;

        FavoritesListBox.AddHandler(PointerPressedEvent, OnFavoritePointerPressed, RoutingStrategies.Tunnel);
        FavoritesListBox.AddHandler(PointerMovedEvent, OnFavoritePointerMoved);
        FavoritesListBox.AddHandler(PointerReleasedEvent, OnFavoritePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// Completes once the view model has been initialized and the saved window state applied (after the window
    /// loaded). Tests await it before driving the window.
    /// </summary>
    public Task Initialization { get; private set; } = Task.CompletedTask;

    // ---- lifecycle --------------------------------------------------------------------------------------

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ExitRequested -= OnExitRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ExitRequested += OnExitRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized || _viewModel is not { } viewModel)
        {
            return;
        }

        _initialized = true;
        Initialization = InitializeAsync(viewModel);
    }

    private async Task InitializeAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.InitializeAsync();
            ApplySavedBounds(viewModel);
        }
        catch (Exception ex)
        {
            AppLog.Error("The main window could not be initialized.", ex);
        }
    }

    private void ApplySavedBounds(MainWindowViewModel viewModel)
    {
        if (viewModel.SavedWindowWidth is > 0 and var width && viewModel.SavedWindowHeight is > 0 and var height)
        {
            Width = Math.Max(width, MinWidth);
            Height = Math.Max(height, MinHeight);
        }

        if (viewModel.SavedWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// The first close request is cancelled so the settings can be written; the window closes itself once the
    /// view model finished shutting down (M11).
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            // The application is going away whatever we answer: save synchronously (the settings service never
            // resumes on the UI thread, so blocking here cannot deadlock) and let the window close.
            _shutdownCompleted = true;
            SaveStateBlocking();
            return;
        }

        e.Cancel = true;
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        try
        {
            if (_viewModel is { } viewModel)
            {
                if (!await viewModel.ConfirmCloseAsync())
                {
                    _shuttingDown = false;
                    return;
                }

                viewModel.RememberWindowBounds(ClientSize.Width, ClientSize.Height, WindowState == WindowState.Maximized);
                await viewModel.ShutdownAsync();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Shutting down failed.", ex);
        }
        finally
        {
            _shutdownCompleted = true;
        }

        // Posted rather than called here: when the shutdown completed synchronously this handler is still
        // inside the platform's Closing callback, and closing from there would nest two close operations.
        Dispatcher.UIThread.Post(Close);
    }

    private void SaveStateBlocking()
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            // The application or the session is going away and there is nobody left to ask: stop a running
            // batch and give the engine its grace period so the partially written temporary file is removed
            // rather than left next to the destination.
            if (viewModel.Operations.IsRunning)
            {
                viewModel.Operations.Cancel();
                viewModel.Operations.WhenIdleAsync().WaitAsync(ShutdownGracePeriod).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            AppLog.Warning("A file operation did not stop before the application closed.");
        }

        try
        {
            viewModel.RememberWindowBounds(ClientSize.Width, ClientSize.Height, WindowState == WindowState.Maximized);
            viewModel.SaveStateAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be saved on shutdown.", ex);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel?.Dispose();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Close();
    }

    // ---- keyboard shortcuts -----------------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentLanguage))
        {
            RelayoutAfterLanguageChange();
        }
    }

    /// <summary>
    /// Re-measures the whole window after a language change. Text that grew or shrank leaves its container
    /// arranged for the old word, so the new one is drawn clipped until every layout node is invalidated.
    /// This hangs off this window's own view model rather than a static event: a static one would keep every
    /// window ever created alive and fan each switch out to all of them.
    /// </summary>
    private void RelayoutAfterLanguageChange()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var layoutable in this.GetVisualDescendants().OfType<Layoutable>())
            {
                layoutable.InvalidateMeasure();
            }

            InvalidateMeasure();
            UpdateLayout();
        }, DispatcherPriority.Render);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && _viewModel is { } viewModel)
        {
            var editing = IsTextEditorFocused();

            foreach (var (gesture, resolve) in ShortcutMap)
            {
                if (!gesture.Matches(e))
                {
                    continue;
                }

                // The clipboard and select-all gestures belong to the text box while the user is typing in one.
                // The design note above assumes the focused control consumes them first, which only holds when
                // the gesture is the platform's own: on macOS a TextBox consumes Cmd+A/C/V/X and lets the Ctrl
                // aliases bubble, so without this a Ctrl+V meant for the path box would paste files into the
                // folder instead. Only these four are skipped; Ctrl+F and the rest still toggle their panel
                // feature from inside the box they act on.
                if (editing && IsTextEditingGesture(gesture))
                {
                    break;
                }

                var command = resolve(viewModel);
                if (command?.CanExecute(null) == true)
                {
                    command.Execute(null);
                }

                e.Handled = true;
                break;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// The gestures a text box implements itself, and which therefore must not act on files while one has focus.
    /// </summary>
    private static bool IsTextEditingGesture(KeyGesture gesture)
    {
        return gesture.Key is Key.A or Key.C or Key.V or Key.X
               && (gesture.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Meta)) == KeyModifiers.None;
    }

    /// <summary>
    /// True when the keyboard focus is inside a control that edits text, so the letter shortcuts belong to it.
    /// The path box is an AutoCompleteBox, whose editor is a TextBox in its template, hence the ancestor walk.
    /// </summary>
    private bool IsTextEditorFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

        return focused is TextBox
               || (focused as Visual)?.FindAncestorOfType<AutoCompleteBox>() is not null;
    }

    // ---- panels and tabs --------------------------------------------------------------------------------

    private void LeftPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _viewModel?.SetActivePanel(_viewModel.LeftPanel);
    }

    private void RightPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _viewModel?.SetActivePanel(_viewModel.RightPanel);
    }

    /// <summary>
    /// Clicking a tab selects it. The close button inside the tab handles its own press, so it never reaches here.
    /// </summary>
    private void Tab_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: TabViewModel tab } && _viewModel is { } viewModel && !ReferenceEquals(viewModel.CurrentTab, tab))
        {
            viewModel.SelectTabCommand.Execute(tab);
        }
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { DataContext = new AboutViewModel() };
        about.ShowDialog(this);
    }

    // ---- favorites: click navigates, drag reorders ------------------------------------------------------

    private void Favorites_Tapped(object? sender, TappedEventArgs e)
    {
        if (FavoriteFromSource(e.Source) is { } favorite && _viewModel?.ActivePanel is { } panel)
        {
            panel.NavigateToFavoriteCommand.Execute(favorite);
            ClearFavoriteSelectionLater();
            e.Handled = true;
        }
    }

    private void Favorites_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        // A click navigates and then clears the selection, but keyboard focus stays on the clicked row, so
        // SelectedItem alone would miss the favorite the user is actually looking at and Delete would fall
        // through to the window and delete files in the panel instead.
        var favorite = FavoritesListBox.SelectedItem as FavoriteItem ?? FavoriteFromSource(e.Source);
        if (favorite is null)
        {
            // Still claim the keys while the favorites list has focus: a stray Delete must never reach the
            // file shortcut of the window.
            e.Handled = e.Key is Key.Enter or Key.Delete;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ActivePanel?.NavigateToFavoriteCommand.Execute(favorite);
                e.Handled = true;
                break;

            case Key.Delete:
                // Delete on a favorite removes the favorite; it must not reach the file shortcut of the window.
                viewModel.RemoveFavoriteCommand.Execute(favorite);
                e.Handled = true;
                break;
        }
    }

    private void FavoritesMenu_Closed(object? sender, RoutedEventArgs e)
    {
        ClearFavoriteSelectionLater();
    }

    private void OnFavoritePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _draggedFavorite = null;
        _favoritePressPoint = null;
        _reorderingFavorites = false;

        var point = e.GetCurrentPoint(FavoritesListBox);
        if (point.Properties.IsLeftButtonPressed && FavoriteFromSource(e.Source) is { } favorite)
        {
            _draggedFavorite = favorite;
            _favoritePressPoint = point.Position;
        }
    }

    private void OnFavoritePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_favoritePressPoint is not { } start || _draggedFavorite is null || _reorderingFavorites)
        {
            return;
        }

        var point = e.GetCurrentPoint(FavoritesListBox);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _favoritePressPoint = null;
            return;
        }

        var delta = point.Position - start;
        if (Math.Abs(delta.X) < ReorderThreshold && Math.Abs(delta.Y) < ReorderThreshold)
        {
            return;
        }

        _reorderingFavorites = true;
        e.Pointer.Capture(FavoritesListBox);
        FavoritesListBox.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private async void OnFavoritePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _draggedFavorite;
        var reordering = _reorderingFavorites;
        _draggedFavorite = null;
        _favoritePressPoint = null;
        _reorderingFavorites = false;

        if (!reordering || dragged is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        FavoritesListBox.Cursor = Cursor.Default;
        e.Handled = true;

        var targetIndex = FavoriteIndexAt(e.GetPosition(FavoritesListBox));
        if (targetIndex < 0 || _viewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            await viewModel.MoveFavoriteAsync(dragged, targetIndex);
        }
        catch (Exception ex)
        {
            AppLog.Error("The favorite could not be moved.", ex);
        }
    }

    private int FavoriteIndexAt(Point position)
    {
        var count = FavoritesListBox.ItemCount;
        if (count == 0)
        {
            return -1;
        }

        if (FavoritesListBox.InputHitTest(position) is Visual visual
            && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } row)
        {
            var index = FavoritesListBox.IndexFromContainer(row);
            if (index >= 0)
            {
                return index;
            }
        }

        return position.Y < 0 ? 0 : count - 1;
    }

    private static FavoriteItem? FavoriteFromSource(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as FavoriteItem;
    }

    /// <summary>The favorites list is a launcher, not a selection: the highlight is removed once the click was handled.</summary>
    private void ClearFavoriteSelectionLater()
    {
        Dispatcher.UIThread.Post(() => FavoritesListBox.SelectedItem = null, DispatcherPriority.Background);
    }
}
