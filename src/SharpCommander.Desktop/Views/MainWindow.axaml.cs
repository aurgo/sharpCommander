using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
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

    /// <summary>
    /// The shortcuts, in evaluation order. They are dispatched from <see cref="OnKeyDown"/> so a focused text
    /// box keeps its own keys (Delete, Backspace, Ctrl+A/C/V/X): Avalonia's KeyBindings run before the focused
    /// control sees the key, which is not what a file manager wants while the user edits a path.
    /// </summary>
    private static readonly (KeyGesture Gesture, Func<MainWindowViewModel, ICommand?> Command)[] Shortcuts =
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
        (KeyGesture.Parse("Ctrl+A"), vm => vm.SelectAllCommand),
        (KeyGesture.Parse("Ctrl+C"), vm => vm.CopyToClipboardCommand),
        (KeyGesture.Parse("Ctrl+X"), vm => vm.CutToClipboardCommand),
        (KeyGesture.Parse("Ctrl+V"), vm => vm.PasteFromClipboardCommand),
        (KeyGesture.Parse("Ctrl+R"), vm => vm.RefreshCommand),
        (KeyGesture.Parse("Ctrl+B"), vm => vm.ToggleFavoritesPanelCommand),
        (KeyGesture.Parse("Ctrl+D"), vm => vm.ToggleFavoriteCommand),
        (KeyGesture.Parse("Ctrl+F"), vm => vm.ToggleSearchCommand),
        (KeyGesture.Parse("Ctrl+H"), vm => vm.ActivePanel?.ToggleShowHiddenFilesCommand),
        (KeyGesture.Parse("Ctrl+Shift+F"), vm => vm.ShowAdvancedSearchCommand),
        (KeyGesture.Parse("Ctrl+M"), vm => vm.ShowMassRenameCommand),
        (KeyGesture.Parse("Ctrl+Shift+H"), vm => vm.CalculateHashCommand),
        (KeyGesture.Parse("Ctrl+T"), vm => vm.NewTabCommand),
        (KeyGesture.Parse("Ctrl+W"), vm => vm.CloseCurrentTabCommand),
        (KeyGesture.Parse("Ctrl+Tab"), vm => vm.NextTabCommand),
        (KeyGesture.Parse("Ctrl+Shift+Tab"), vm => vm.PreviousTabCommand)
    ];

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
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ExitRequested += OnExitRequested;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && _viewModel is { } viewModel)
        {
            foreach (var (gesture, resolve) in Shortcuts)
            {
                if (!gesture.Matches(e))
                {
                    continue;
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

    // ---- panels and tabs --------------------------------------------------------------------------------

    private void LeftPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _viewModel?.SetActivePanel(_viewModel.LeftPanel);
    }

    private void RightPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        _viewModel?.SetActivePanel(_viewModel.RightPanel);
    }

    private void TabStrip_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The strip raises this while the XAML is still being populated, before the named field is assigned.
        if (sender is TabStrip { SelectedItem: TabViewModel tab } && _viewModel is { } viewModel && !ReferenceEquals(viewModel.CurrentTab, tab))
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
        if (FavoritesListBox.SelectedItem is not FavoriteItem favorite || _viewModel is not { } viewModel)
        {
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
