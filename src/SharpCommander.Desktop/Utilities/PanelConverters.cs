using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// The brushes of the favorites star. They are created once: converters must not allocate per row (L2), so they
/// return these cached immutable objects.
/// </summary>
public static class PanelBrushes
{
    /// <summary>Star color of a folder that is a favorite.</summary>
    public static IImmutableSolidColorBrush FavoriteOn { get; } = new ImmutableSolidColorBrush(Color.FromRgb(255, 193, 7));

    /// <summary>Star color of a folder that is not a favorite.</summary>
    public static IImmutableSolidColorBrush FavoriteOff { get; } = new ImmutableSolidColorBrush(Color.FromRgb(128, 128, 128));
}

/// <summary>
/// Value converters used by the panel and main window XAML. Each one is a singleton exposed through a static
/// property so the compiled bindings reference it with x:Static.
/// </summary>
public static class PanelConverters
{
    /// <summary>
    /// Entry to the text of the size column: the size for files and drives, "&lt;DIR&gt;" for folders until one
    /// has been measured.
    /// </summary>
    public static IValueConverter EntrySize { get; } = new EntrySizeConverter();

    /// <summary>Entry to the text of the modified column; empty for drives and unknown dates.</summary>
    public static IValueConverter EntryModified { get; } = new EntryModifiedConverter();

    /// <summary>Hidden flag to the row opacity, so hidden entries are dimmed.</summary>
    public static IValueConverter HiddenOpacity { get; } = new HiddenOpacityConverter();

    /// <summary>Favorite flag to the filled or outlined star geometry.</summary>
    public static IValueConverter FavoriteIcon { get; } = new FavoriteIconConverter();

    /// <summary>Favorite flag to the star brush.</summary>
    public static IValueConverter FavoriteBrush { get; } = new FavoriteBrushConverter();

    /// <summary>True when an integer is greater than one (used to show tab close buttons).</summary>
    public static IValueConverter MoreThanOne { get; } = new MoreThanOneConverter();

    private sealed class EntrySizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                FileSystemEntry { EntryType: FileSystemEntryType.File } file => FileSizeFormatter.FormatForDisplay(file.Size),
                FileSystemEntry { EntryType: FileSystemEntryType.Drive } drive => FileSizeFormatter.Format(drive.Size),
                FileSystemEntry { EntryType: FileSystemEntryType.Directory, CalculatedSize: { } measured } => FileSizeFormatter.FormatForDisplay(measured),
                FileSystemEntry { EntryType: FileSystemEntryType.Directory } => "<DIR>",
                _ => string.Empty
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class EntryModifiedConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not FileSystemEntry entry || entry.EntryType == FileSystemEntryType.Drive || entry.LastModified == default)
            {
                return string.Empty;
            }

            return entry.LastModified.ToString("g", culture);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class HiddenOpacityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? 0.55 : 1.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FavoriteIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? EntryIcons.StarFilled : EntryIcons.StarOutline;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FavoriteBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true ? PanelBrushes.FavoriteOn : PanelBrushes.FavoriteOff;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class MoreThanOneConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is int count && count > 1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
