using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Utilities;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// File panel user control for displaying directory contents.
/// </summary>
public partial class FilePanelView : UserControl
{
    public FilePanelView()
    {
        InitializeComponent();
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FilePanelViewModel viewModel)
        {
            viewModel.OpenSelectedCommand.Execute(null);
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
            }
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
