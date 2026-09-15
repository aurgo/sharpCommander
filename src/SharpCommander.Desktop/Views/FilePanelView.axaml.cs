using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// The file panel view. It keeps the list selection and the view model selection in sync in both directions,
/// implements type-ahead through <see cref="InputElement.TextInput"/> (so it works with any keyboard layout),
/// starts system drags of the selected files and accepts dropped files through the operations service.
/// </summary>
public partial class FilePanelView : UserControl
{
    private const double DragThreshold = 8;
    private const string DropTargetClass = "drop-target";
    private const string ActiveClass = "active";
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Application format carried next to the file items of a drag, so drops between SharpCommander panels
    /// work even when the platform does not hand storage items back (and so internal drags are recognised).
    /// </summary>
    internal static readonly DataFormat<string> InternalPathsFormat = DataFormat.CreateStringApplicationFormat("SharpCommander.DragPaths");

    private FilePanelViewModel? _viewModel;
    private MainWindowViewModel? _mainViewModel;
    private bool _syncingToList;
    private bool _syncingFromList;
    private bool _selectionSyncScheduled;
    private string _typeAhead = string.Empty;
    private long _typeAheadTime;
    private Point? _pressPoint;
    private bool _pressedOnSelectedRow;
    private bool _dragging;

    /// <summary>
    /// Whether the list held keyboard focus when the current listing started. A navigation replaces every entry,
    /// so the ListBox recycles the focused row and Avalonia clears keyboard focus to null before the reveal
    /// callback runs; without this flag the callback cannot tell "the user was driving the list with the
    /// keyboard" from "the user is typing in the path box", and the cursor keys go dead after every Enter.
    /// </summary>
    private bool _hadKeyboardFocus;

    public FilePanelView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnPanelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileListBox.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        FileListBox.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, OnListTextInput);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        PathBox.AddHandler(KeyDownEvent, OnPathBoxKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Enter and F4 in the path box. Enter navigates only once the suggestion drop-down is closed, so the first
    /// Enter commits the highlighted suggestion and the second one goes to it. F4 is claimed before the
    /// AutoCompleteBox, which otherwise handles it as a drop-down toggle and swallows the Edit shortcut the
    /// menu, the toolbar and the function bar all advertise.
    /// </summary>
    private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when !PathBox.IsDropDownOpen:
                if (_viewModel?.NavigateToPathCommand is { } navigate && navigate.CanExecute(null))
                {
                    navigate.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.F4:
                if (_mainViewModel?.EditCommand is { } edit && edit.CanExecute(null))
                {
                    edit.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }

    // ---- wiring -----------------------------------------------------------------------------------------

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.SelectedEntries.CollectionChanged -= OnViewModelSelectionChanged;
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel.SelectionRevealRequested -= OnSelectionRevealRequested;
            _viewModel = null;
        }

        if (DataContext is FilePanelViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.SelectedEntries.CollectionChanged += OnViewModelSelectionChanged;
            viewModel.FocusRequested += OnFocusRequested;
            viewModel.SelectionRevealRequested += OnSelectionRevealRequested;
            ScheduleSelectionSync();
        }

        UpdateActiveState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (VisualRoot is Window { DataContext: MainWindowViewModel mainViewModel })
        {
            _mainViewModel = mainViewModel;
            mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        }

        UpdateActiveState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            _mainViewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ActivePanel))
        {
            UpdateActiveState();
        }
    }

    /// <summary>
    /// Marks the active panel so the user sees where F5/F6 will act: the header gets the accent underline, and
    /// the list keeps the accent-coloured selection. The other panel's selection goes grey — both lists keep a
    /// selection at all times, and two identical highlights said nothing about which one the keys would reach.
    /// </summary>
    private void UpdateActiveState()
    {
        var isActive = _viewModel is not null && _mainViewModel is not null && ReferenceEquals(_mainViewModel.ActivePanel, _viewModel);
        PanelHeader.Classes.Set(ActiveClass, isActive);
        FileListBox.Classes.Set(ActiveClass, isActive);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FilePanelViewModel.SelectedEntry):
                if (!_syncingFromList)
                {
                    ScheduleSelectionSync();
                }

                break;

            case nameof(FilePanelViewModel.IsSearchActive):
                if (_viewModel?.IsSearchActive == true)
                {
                    Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
                }

                break;

            case nameof(FilePanelViewModel.IsLoading):
                // Captured while the old rows are still in place: once the listing is applied the focused row
                // is gone and IsKeyboardFocusWithin is already false.
                if (_viewModel?.IsLoading == true)
                {
                    _hadKeyboardFocus = FileListBox.IsKeyboardFocusWithin;
                }

                break;
        }
    }

    // ---- selection: view model to list ---------------------------------------------------------------------

    private void OnViewModelSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_syncingFromList)
        {
            ScheduleSelectionSync();
        }
    }

    /// <summary>Applies the view model selection to the list once per dispatcher turn, however many items changed.</summary>
    private void ScheduleSelectionSync()
    {
        if (_selectionSyncScheduled)
        {
            return;
        }

        _selectionSyncScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _selectionSyncScheduled = false;
            SyncSelectionToList();
        });
    }

    private void SyncSelectionToList()
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        _syncingToList = true;
        try
        {
            var indexes = new EntryIndex(viewModel.FilteredEntries);
            var selection = FileListBox.Selection;

            selection.BeginBatchUpdate();
            try
            {
                selection.Clear();
                foreach (var entry in viewModel.SelectedEntries)
                {
                    var index = indexes.IndexOf(entry);
                    if (index >= 0)
                    {
                        selection.Select(index);
                    }
                }

                // The cursor entry is selected on its own only when there is no multi-selection (Select All keeps
                // the cursor on ".." without selecting it); it always becomes the anchor for keyboard navigation.
                if (viewModel.SelectedEntry is { } current)
                {
                    var index = indexes.IndexOf(current);
                    if (index >= 0)
                    {
                        if (viewModel.SelectedEntries.Count == 0)
                        {
                            selection.Select(index);
                        }

                        selection.AnchorIndex = index;
                    }
                }
            }
            finally
            {
                selection.EndBatchUpdate();
            }
        }
        finally
        {
            _syncingToList = false;
        }
    }

    // ---- selection: list to view model ---------------------------------------------------------------------

    private void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingToList || _viewModel is not { } viewModel)
        {
            return;
        }

        var selected = FileListBox.SelectedItems?.OfType<FileSystemEntry>().ToList() ?? [];

        // ".." is a cursor position, never part of a multi-selection.
        if (selected.Count > 1 && selected.FirstOrDefault(entry => entry.EntryType == FileSystemEntryType.ParentDirectory) is { } parent)
        {
            _syncingToList = true;
            try
            {
                FileListBox.SelectedItems?.Remove(parent);
            }
            finally
            {
                _syncingToList = false;
            }

            selected.Remove(parent);
        }

        _syncingFromList = true;
        try
        {
            if (!SameEntries(viewModel.SelectedEntries, selected))
            {
                viewModel.SelectedEntries.Clear();
                foreach (var entry in selected)
                {
                    viewModel.SelectedEntries.Add(entry);
                }
            }

            var current = FileListBox.SelectedItem as FileSystemEntry ?? selected.LastOrDefault();
            if (!ReferenceEquals(viewModel.SelectedEntry, current))
            {
                viewModel.SelectedEntry = current;
            }
        }
        finally
        {
            _syncingFromList = false;
        }
    }

    private static bool SameEntries(IList<FileSystemEntry> a, IList<FileSystemEntry> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var index = 0; index < a.Count; index++)
        {
            if (!ReferenceEquals(a[index], b[index]))
            {
                return false;
            }
        }

        return true;
    }

    // ---- focus and reveal ---------------------------------------------------------------------------------

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(FocusSelectedRow, DispatcherPriority.Loaded);
    }

    private void OnSelectionRevealRequested(object? sender, FileSystemEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FileListBox.ScrollIntoView(entry);
            if (ShouldRestoreKeyboardFocus() && FileListBox.ContainerFromItem(entry) is Control container)
            {
                container.Focus(NavigationMethod.Directional);
                _hadKeyboardFocus = true;
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// True when the cursor row should take keyboard focus after a listing was applied: the list still has it
    /// (an in-place refresh), it had it when the listing started (a navigation, which clears it), or the window
    /// has no focused element at all, which is the state a navigation reset leaves behind. Focus is not stolen
    /// while the user is typing in the path box, the filter box or any other control.
    /// </summary>
    private bool ShouldRestoreKeyboardFocus()
    {
        if (FileListBox.IsKeyboardFocusWithin)
        {
            return true;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is null && _hadKeyboardFocus;
    }

    private void FocusSelectedRow()
    {
        if (_viewModel?.SelectedEntry is { } entry)
        {
            FileListBox.ScrollIntoView(entry);
            if (FileListBox.ContainerFromItem(entry) is Control container)
            {
                container.Focus(NavigationMethod.Directional);
                return;
            }
        }

        FileListBox.Focus(NavigationMethod.Directional);
    }

    // ---- keyboard -----------------------------------------------------------------------------------------

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // Only rows open; a double click on empty space does nothing.
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
        {
            _viewModel.OpenSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                viewModel.OpenSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Back when e.KeyModifiers == KeyModifiers.None:
                viewModel.NavigateUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                _typeAhead = string.Empty;
                if (viewModel.IsSearchActive)
                {
                    viewModel.ClearSearchCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    /// <summary>
    /// Type-ahead: printable text typed while the focus is on the list (or the panel itself) selects the next
    /// entry starting with the buffer; the buffer resets after one second; repeating a character cycles through
    /// the entries starting with it. Text typed into the path or filter boxes is left alone.
    /// </summary>
    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (_viewModel is not { } viewModel || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin || PathBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _typeAheadTime > TypeAheadTimeout.TotalMilliseconds)
        {
            _typeAhead = string.Empty;
        }

        _typeAheadTime = now;
        e.Handled = true;

        var candidate = _typeAhead + e.Text;
        if (viewModel.IncrementalSearch(candidate))
        {
            _typeAhead = candidate;
            return;
        }

        var first = candidate[..1];
        if (candidate.Length > 1 && candidate.All(c => c == candidate[0]) && viewModel.IncrementalSearch(first))
        {
            _typeAhead = first;
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.ClearSearchCommand.Execute(null);
                FocusSelectedRow();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Down:
                FocusSelectedRow();
                e.Handled = true;
                break;
        }
    }

    // ---- pointer: active panel and drag source ----------------------------------------------------------

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _mainViewModel?.SetActivePanel(_viewModel);
        }
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressPoint = null;

        var point = e.GetCurrentPoint(FileListBox);

        // The middle click opens the row under the pointer in a new tab. It is claimed whether or not it landed
        // on a row, so the ListBox never sees it: otherwise it would move the selection, and on X11 the press
        // would also arrive as a primary-selection paste.
        if (point.Properties.IsMiddleButtonPressed)
        {
            OpenInNewTab(RowEntryAt(e.Source));
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: FileSystemEntry entry } row
            || entry.EntryType == FileSystemEntryType.ParentDirectory)
        {
            return;
        }

        _pressPoint = point.Position;
        _pressedOnSelectedRow = row.IsSelected;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressPoint = null;
    }

    /// <summary>The entry of the list row <paramref name="source"/> sits in, or null when it is not on a row.</summary>
    private static FileSystemEntry? RowEntryAt(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: FileSystemEntry entry }
            ? entry
            : null;
    }

    /// <summary>
    /// Hands <paramref name="entry"/> to the main view model to be opened in a new tab. This panel is passed
    /// along so the tab is owned by the side that was clicked, not by whichever side happens to hold the focus.
    /// The middle click and the context menu both come through here; what may be opened is decided there.
    /// </summary>
    private void OpenInNewTab(FileSystemEntry? entry)
    {
        if (entry is null || _viewModel is not { } panel || _mainViewModel is not { } main)
        {
            return;
        }

        // Fire and forget: the view model guards the whole operation and reports failures itself.
        _ = main.OpenInNewTabAsync(panel, entry);
    }

    /// <summary>The "Open in New Tab" context menu entry, which acts on the selected row.</summary>
    private void OnOpenInNewTabClick(object? sender, RoutedEventArgs e)
    {
        OpenInNewTab(_viewModel?.SelectedEntry);
    }

    private async void ListBox_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _dragging || !_pressedOnSelectedRow || _viewModel is not { } viewModel)
        {
            return;
        }

        var point = e.GetCurrentPoint(FileListBox);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pressPoint = null;
            return;
        }

        var delta = point.Position - start;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _pressPoint = null;
        var items = viewModel.GetSelectedItems();
        if (items.Count == 0)
        {
            return;
        }

        _dragging = true;
        try
        {
            var transfer = await BuildDragDataAsync(items);
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            AppLog.Warning("The drag operation failed.", ex);
        }
        finally
        {
            _dragging = false;
        }
    }

    private async Task<DataTransfer> BuildDragDataAsync(IReadOnlyList<FileSystemEntry> items)
    {
        var transfer = new DataTransfer();

        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
        {
            foreach (var item in items)
            {
                IStorageItem? storageItem = item.EntryType == FileSystemEntryType.Directory
                    ? await storage.TryGetFolderFromPathAsync(item.FullPath)
                    : await storage.TryGetFileFromPathAsync(item.FullPath);

                if (storageItem is not null)
                {
                    transfer.Add(DataTransferItem.CreateFile(storageItem));
                }
            }
        }

        transfer.Add(DataTransferItem.Create(InternalPathsFormat, string.Join('\n', items.Select(item => item.FullPath))));
        return transfer;
    }

    // ---- drop target --------------------------------------------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var effect = ResolveDropEffect(e);
        e.DragEffects = effect;
        FileListBox.Classes.Set(DropTargetClass, effect != DragDropEffects.None);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        FileListBox.Classes.Set(DropTargetClass, false);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        FileListBox.Classes.Set(DropTargetClass, false);

        var effect = ResolveDropEffect(e);
        e.DragEffects = effect;
        if (effect == DragDropEffects.None || _viewModel is not { } viewModel)
        {
            return;
        }

        e.Handled = true;
        var paths = GetDroppedPaths(e.DataTransfer);
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var move = effect == DragDropEffects.Move;
            if (_mainViewModel is not null)
            {
                await _mainViewModel.DropAsync(viewModel, paths, move);
            }
            else
            {
                await viewModel.DropAsync(paths, move);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("The dropped files could not be processed.", ex);
        }
    }

    /// <summary>
    /// Copy with Ctrl, move otherwise for drags between SharpCommander panels; drops from other applications
    /// always copy (the source might otherwise delete the originals). Nothing can be dropped on the Computer view.
    /// </summary>
    private DragDropEffects ResolveDropEffect(DragEventArgs e)
    {
        if (_viewModel is not { } viewModel || viewModel.IsRootView || string.IsNullOrEmpty(viewModel.CurrentPath))
        {
            return DragDropEffects.None;
        }

        var data = e.DataTransfer;
        var internalDrag = data.Contains(InternalPathsFormat);
        if (!internalDrag && !data.Contains(DataFormat.File))
        {
            return DragDropEffects.None;
        }

        var allowed = e.DragEffects;
        var copy = !internalDrag || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (copy)
        {
            return allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        if (allowed.HasFlag(DragDropEffects.Move))
        {
            return DragDropEffects.Move;
        }

        return allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private static List<string> GetDroppedPaths(IDataTransfer data)
    {
        var paths = new List<string>();

        try
        {
            if (data.TryGetFiles() is { } files)
            {
                foreach (var file in files)
                {
                    if (file.TryGetLocalPath() is { Length: > 0 } path)
                    {
                        paths.Add(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("The dropped storage items could not be read.", ex);
        }

        if (paths.Count == 0 && data.TryGetValue(InternalPathsFormat) is { Length: > 0 } text)
        {
            paths.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return paths;
    }

    /// <summary>Path to index lookup over the visible entries, built once per synchronization.</summary>
    private sealed class EntryIndex
    {
        private readonly IList<FileSystemEntry> _entries;
        private Dictionary<string, int>? _byPath;

        public EntryIndex(IList<FileSystemEntry> entries)
        {
            _entries = entries;
        }

        public int IndexOf(FileSystemEntry entry)
        {
            if (_entries.Count <= 64)
            {
                return _entries.IndexOf(entry);
            }

            if (_byPath is null)
            {
                _byPath = new Dictionary<string, int>(_entries.Count, PathUtils.PathComparer);
                for (var index = 0; index < _entries.Count; index++)
                {
                    _byPath.TryAdd(_entries[index].FullPath, index);
                }
            }

            return _byPath.TryGetValue(entry.FullPath, out var found) && ReferenceEquals(_entries[found], entry) ? found : _entries.IndexOf(entry);
        }
    }
}
