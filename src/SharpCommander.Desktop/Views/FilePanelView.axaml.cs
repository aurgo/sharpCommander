using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
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

    public FilePanelView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, ListBox_Drop);
        AddHandler(DragDrop.DragOverEvent, ListBox_DragOver);
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FilePanelViewModel viewModel)
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }

    private void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FilePanelViewModel viewModel || sender is not ListBox listBox)
        {
            return;
        }

        // Sync selected items to ViewModel
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

        // Handle alphanumeric keys for incremental search
        if ((e.Key >= Key.A && e.Key <= Key.Z) || 
            (e.Key >= Key.D0 && e.Key <= Key.D9) ||
            (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9))
        {
            var keyChar = GetKeyChar(e);
            if (!string.IsNullOrEmpty(keyChar))
            {
                _incrementalSearchBuffer += keyChar;
                _lastKeyPressTime = currentTime;

                // Find and select the first matching entry
                var matchingEntry = viewModel.FilteredEntries
                    .FirstOrDefault(entry => 
                        entry.Name.StartsWith(_incrementalSearchBuffer, StringComparison.OrdinalIgnoreCase));

                if (matchingEntry != null)
                {
                    viewModel.SelectedEntry = matchingEntry;
                    e.Handled = true;
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
        return string.Empty;
    }

    private void ListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
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

    private async void ListBox_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FilePanelViewModel viewModel)
        {
            return;
        }

        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        var filePaths = files.Select(f => f.Path.LocalPath).ToList();
        
        // Determine the target directory
        string targetDirectory;
        
        // Try to get the item under the cursor
        if (sender is ListBox listBox)
        {
            var position = e.GetPosition(listBox);
            var hitTest = listBox.InputHitTest(position);
            
            // If dropped on an item, check if it's a directory
            if (hitTest is Visual visual)
            {
                var item = visual.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is FileSystemEntry entry && 
                    entry.EntryType == FileSystemEntryType.Directory)
                {
                    targetDirectory = entry.FullPath;
                }
                else
                {
                    targetDirectory = viewModel.CurrentPath;
                }
            }
            else
            {
                targetDirectory = viewModel.CurrentPath;
            }
        }
        else
        {
            targetDirectory = viewModel.CurrentPath;
        }

        if (string.IsNullOrEmpty(targetDirectory))
        {
            return;
        }

        // Perform the operation based on drag effect
        var isMove = e.DragEffects == DragDropEffects.Move;
        
        // Notify that we're handling this via the MainWindowViewModel
        // We'll raise an event or call a method to handle this
        viewModel.StatusText = isMove 
            ? $"Moving {filePaths.Count} item(s)..." 
            : $"Copying {filePaths.Count} item(s)...";
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
        if (value is not long bytes)
        {
            return string.Empty;
        }

        return FileSizeFormatter.FormatForDisplay(bytes);
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
