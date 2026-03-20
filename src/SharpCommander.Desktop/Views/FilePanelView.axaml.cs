using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Utilities;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// File panel user control for displaying directory contents.
/// </summary>
public partial class FilePanelView : UserControl
{
    private string _incrementalSearchBuffer = string.Empty;
    private int _lastKeyPressTime;
    private const int SearchBufferTimeoutMs = 1000; // Reset search buffer after 1 second
    private Point? _dragStartPoint;
    private bool _isDragging;
    private FilePanelViewModel? _subscribedViewModel;

    public FilePanelView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, ListBox_Drop);
        AddHandler(DragDrop.DragOverEvent, ListBox_DragOver);
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous ViewModel to avoid accumulating handlers
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.FocusRequested -= OnViewModelFocusRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is FilePanelViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.FocusRequested += OnViewModelFocusRequested;
            SyncSelectionToUI(viewModel);
        }
    }

    private void OnViewModelFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ListBox>("FileListBox") is ListBox listBox)
            {
                if (listBox.SelectedItem != null && listBox.ContainerFromItem(listBox.SelectedItem) is Control container)
                {
                    container.Focus(NavigationMethod.Directional);
                }
                else
                {
                    listBox.Focus(NavigationMethod.Directional);
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FilePanelViewModel.FilteredEntries) &&
            DataContext is FilePanelViewModel viewModel)
        {
            SyncSelectionToUI(viewModel);
        }
    }

    private void SyncSelectionToUI(FilePanelViewModel viewModel)
    {
        if (this.FindControl<ListBox>("FileListBox") is not ListBox listBox) return;
        
        _isUpdatingSelectionFromCode = true;
        try
        {
            listBox.SelectedItems?.Clear();
            foreach (var item in viewModel.SelectedEntries)
            {
                listBox.SelectedItems?.Add(item);
            }
            
            if (viewModel.SelectedEntry != null)
            {
                // Synchronize single selection and focus anchor
                var index = viewModel.FilteredEntries.IndexOf(viewModel.SelectedEntry);
                if (index != -1)
                {
                    listBox.SelectedIndex = index;
                }
                else
                {
                    listBox.SelectedItem = viewModel.SelectedEntry;
                }
                
                listBox.ScrollIntoView(viewModel.SelectedEntry);
            }
        }
        finally
        {
            _isUpdatingSelectionFromCode = false;
        }
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FilePanelViewModel viewModel)
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }



    private bool _isUpdatingSelectionFromCode;

    private void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelectionFromCode) return;
        
        if (DataContext is not FilePanelViewModel viewModel || sender is not ListBox listBox)
        {
            return;
        }

        // To prevent loop when viewModel updates selection
        _isUpdatingSelectionFromCode = true;
        try
        {
            viewModel.SelectedEntries.Clear();
            if (listBox.SelectedItems != null)
            {
                foreach (var item in listBox.SelectedItems)
                {
                    if (item is FileSystemEntry entry)
                    {
                        viewModel.SelectedEntries.Add(entry);
                    }
                }
            }
        }
        finally
        {
            _isUpdatingSelectionFromCode = false;
        }
    }

    private void ListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is FilePanelViewModel viewModel)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    viewModel.OpenSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Back:
                    viewModel.NavigateUpCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.F5:
                    viewModel.RefreshCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    // Don't handle Delete here - let it bubble up to MainWindow
                    // so the DeleteCommand can be executed
                    break;
                case Key.F2:
                    viewModel.RenameSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                default:
                    // Handle incremental search (type to navigate)
                    HandleIncrementalSearch(e, viewModel);
                    break;
            }
        }
    }

    private void HandleIncrementalSearch(KeyEventArgs e, FilePanelViewModel viewModel)
    {
        // Check if enough time has passed to reset the search buffer
        var currentTime = Environment.TickCount;
        if (unchecked(currentTime - _lastKeyPressTime) > SearchBufferTimeoutMs)
        {
            _incrementalSearchBuffer = string.Empty;
        }

        // Ignore keys with modifiers (Ctrl, Alt, Meta) - except Shift
        if (e.KeyModifiers != KeyModifiers.None && e.KeyModifiers != KeyModifiers.Shift)
        {
            return;
        }

        // Convert key to character (if possible)
        var keyChar = GetKeyChar(e);
        
        // Handle alphanumeric and common punctuation for incremental search
        if (!string.IsNullOrEmpty(keyChar))
        {
            // Always mark as handled to prevent bubbling up to menu or other controls
            // This is CRITICAL to prevent the Alt-key access key behavior from stealing focus
            e.Handled = true;

            _incrementalSearchBuffer += keyChar;
            _lastKeyPressTime = currentTime;

            // Find and select the first matching entry
            var matchingEntry = viewModel.FilteredEntries
                .FirstOrDefault(entry => 
                    entry.Name.StartsWith(_incrementalSearchBuffer, StringComparison.OrdinalIgnoreCase));

            if (matchingEntry != null)
            {
                // El flag debe activarse ANTES de asignar SelectedEntry porque el binding
                // bidireccional (SelectedItem ↔ SelectedEntry) dispararía SelectionChanged
                // inmediatamente, corrompiendo SelectedEntries con un estado intermedio.
                _isUpdatingSelectionFromCode = true;
                try
                {
                    viewModel.SelectedEntry = matchingEntry;
                    viewModel.SelectedEntries.Clear();
                    viewModel.SelectedEntries.Add(matchingEntry);

                    // Sincronizar ancla de teclado en el ListBox para que las flechas
                    // continúen desde el elemento encontrado y no desde la posición anterior.
                    if (this.FindControl<ListBox>("FileListBox") is ListBox listBox)
                    {
                        var index = viewModel.FilteredEntries.IndexOf(matchingEntry);
                        if (index != -1)
                            listBox.SelectedIndex = index;
                        listBox.ScrollIntoView(matchingEntry);

                        // SelectedIndex actualiza la selección pero NO el foco de teclado.
                        // La navegación con flechas parte del ListBoxItem que tiene foco lógico,
                        // por lo que hay que enfocarlo explícitamente después de que
                        // ScrollIntoView haya realizado el container virtualizado.
                        if (index != -1)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (listBox.ContainerFromIndex(index) is ListBoxItem item)
                                    item.Focus(NavigationMethod.Directional);
                            }, DispatcherPriority.Loaded);
                        }
                    }
                }
                finally
                {
                    _isUpdatingSelectionFromCode = false;
                }
            }
        }
    }

    private string GetKeyChar(KeyEventArgs e)
    {
        // Convert key to character
        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            return ((char)('a' + (e.Key - Key.A))).ToString();
        }
        else if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            return ((char)('0' + (e.Key - Key.D0))).ToString();
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            return ((char)('0' + (e.Key - Key.NumPad0))).ToString();
        }
        
        // Handle common punctuation and symbols
        return e.Key switch
        {
            Key.Space => " ",
            Key.OemPeriod or Key.Decimal => ".",
            Key.OemComma => ",",
            Key.OemMinus or Key.Subtract => "-",
            Key.OemPlus or Key.Add => "+",
            Key.Oem5 => "_", // Usually underscore/backslash
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.Oem1 => ";",
            Key.Oem7 => "'",
            _ => string.Empty
        };
    }

    private void ListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FilePanelViewModel viewModel)
        {
            // Sync active panel to MainWindow
            if (this.VisualRoot is Window window && window.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.SetActivePanel(viewModel);
            }
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
        }
    }

    private async void ListBox_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint.HasValue && 
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && 
            !_isDragging &&
            DataContext is FilePanelViewModel viewModel)
        {
            var currentPoint = e.GetPosition(this);
            var diff = _dragStartPoint.Value - currentPoint;
            
            // Check if the pointer has moved enough to start dragging
            if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
            {
                var selectedItems = viewModel.GetSelectedItems();
                if (selectedItems.Count > 0)
                {
                    _isDragging = true;
                    
                    // Create data object with file paths
                    var dataObject = new DataObject();
                    var files = selectedItems.Select(item => item.FullPath).ToArray();
                    dataObject.Set(DataFormats.Files, files);
                    
                    // Start drag operation
                    await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
                    
                    _dragStartPoint = null;
                    _isDragging = false;
                }
            }
        }
    }

    private void ListBox_DragOver(object? sender, DragEventArgs e)
    {
        // Only allow file drops
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control) 
                ? DragDropEffects.Copy 
                : DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void ListBox_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FilePanelViewModel viewModel)
        {
            return;
        }

        // Accept files dropped from external sources (e.g., Windows Explorer)
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var fileCount = files.Count();
                viewModel.StatusText = $"Dropped {fileCount} item(s). Use Ctrl+C/V or F5/F6 to copy/move files between panels.";
            }
            e.Handled = true;
        }
    }
}

/// <summary>
/// Converts file size to human-readable format.
/// </summary>
public sealed class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileSystemEntry entry)
        {
            return string.Empty;
        }

        if (entry.EntryType == FileSystemEntryType.Directory || 
            entry.EntryType == FileSystemEntryType.Drive || 
            entry.EntryType == FileSystemEntryType.ParentDirectory)
        {
            return "<DIR>";
        }

        return FileSizeFormatter.FormatForDisplay(entry.Size);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts file system entry type to icon path data.
/// </summary>
public sealed class FileIconConverter : IValueConverter
{
    public static readonly FileIconConverter Instance = new();

    // Icon path data
    private const string FolderIcon = "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2Z";
    private const string FileIcon = "M13 9V3.5L18.5 9M6 2c-1.11 0-2 .89-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6H6Z";
    private const string DriveIcon = "M6 2c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2H6Zm0 2h12v16H6V4Zm6 3a5 5 0 0 0-5 5 5 5 0 0 0 5 5 5 5 0 0 0 5-5 5 5 0 0 0-5-5Zm0 2a3 3 0 0 1 3 3 3 3 0 0 1-3 3 3 3 0 0 1-3-3 3 3 0 0 1 3-3Z";
    private const string ParentIcon = "M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2Z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var iconData = value switch
        {
            FileSystemEntryType.Directory => FolderIcon,
            FileSystemEntryType.Drive => DriveIcon,
            FileSystemEntryType.ParentDirectory => ParentIcon,
            FileSystemEntryType.File => FileIcon,
            _ => FileIcon
        };

        return Geometry.Parse(iconData);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts file system entry type to color.
/// </summary>
public sealed class FileColorConverter : IValueConverter
{
    public static readonly FileColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            FileSystemEntryType.Directory => new SolidColorBrush(Color.FromRgb(255, 204, 0)),
            FileSystemEntryType.Drive => new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            FileSystemEntryType.ParentDirectory => new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts favorite boolean to star icon.
/// </summary>
public sealed class FavoriteIconConverter : IValueConverter
{
    private const string StarFilled = "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27Z";
    private const string StarOutline = "M12 15.39l-3.76 2.27.99-4.28-3.32-2.88 4.38-.37L12 6.09l1.71 4.04 4.38.37-3.32 2.88.99 4.28M22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.64-7.03L22 9.24Z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isFavorite = value is true;
        return Geometry.Parse(isFavorite ? StarFilled : StarOutline);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts favorite boolean to color.
/// </summary>
public sealed class FavoriteColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isFavorite = value is true;
        return isFavorite 
            ? new SolidColorBrush(Color.FromRgb(255, 193, 7))  // Gold
            : new SolidColorBrush(Color.FromRgb(128, 128, 128)); // Gray
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
