using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// The colour icons of folders, files and drives: two-tone shapes with a soft gradient, as current Windows and
/// macOS draw them, in place of the flat single-colour glyphs that made the lists look like a file manager from the
/// nineties. Each is drawn on a 16-unit grid, so its edges fall on whole pixels at the 16 px of a file list, and is
/// built once and shared by every row that shows it.
/// </summary>
public static class EntryImages
{
    // A folder is its back (with the tab), a sheet of paper and the front, lighter than the back.
    private const string FolderBackPath =
        "M2.5 1.5H6.1C6.5 1.5 6.88 1.68 7.13 2L8 3H13.5C14.33 3 15 3.67 15 4.5V12.5C15 13.33 14.33 14 13.5 14H2.5C1.67 14 1 13.33 1 12.5V3C1 2.17 1.67 1.5 2.5 1.5Z";

    private const string FolderPaperPath = "M2.25 4H13.75V8H2.25Z";

    private const string FolderFrontPath =
        "M1 6.5C1 5.95 1.45 5.5 2 5.5H14C14.55 5.5 15 5.95 15 6.5V12.5C15 13.33 14.33 14 13.5 14H2.5C1.67 14 1 13.33 1 12.5Z";

    // A page with a folded corner. The half units put its 1 px outline exactly on a row of pixels.
    private const string PagePath =
        "M4.5 1.5H9.79L12.5 4.21V13.5C12.5 14.05 12.05 14.5 11.5 14.5H4.5C3.95 14.5 3.5 14.05 3.5 13.5V2.5C3.5 1.95 3.95 1.5 4.5 1.5Z";

    private const string PageFoldPath = "M9.5 1.5V3.75C9.5 4.16 9.84 4.5 10.25 4.5H12.5";

    // A drive: a light case on a darker base, with a lit indicator.
    private const string DriveCasePath =
        "M3 3.5H13C14.1 3.5 15 4.4 15 5.5V10.5C15 11.6 14.1 12.5 13 12.5H3C1.9 12.5 1 11.6 1 10.5V5.5C1 4.4 1.9 3.5 3 3.5Z";

    private const string DriveBasePath = "M1 9H15V10.5C15 11.6 14.1 12.5 13 12.5H3C1.9 12.5 1 11.6 1 10.5Z";

    private const string DriveLightPath = "M11.25 10.75A1 1 0 1 1 13.25 10.75A1 1 0 1 1 11.25 10.75Z";

    // The glyphs set into the front of the special folders, on the 24-unit grid of Material Design Icons.
    private const string DesktopGlyph = "M21 16H3V4H21M21 2H3C1.89 2 1 2.89 1 4V16A2 2 0 0 0 3 18H10V20H8V22H16V20H14V18H21A2 2 0 0 0 23 16V4C23 2.89 22.1 2 21 2Z";
    private const string DocumentsGlyph = "M13 9H18.5L13 3.5V9M6 2H14L20 8V20A2 2 0 0 1 18 22H6C4.89 22 4 21.1 4 20V4C4 2.89 4.89 2 6 2M15 18V16H6V18H15M18 14V12H6V14H18Z";
    private const string DownloadsGlyph = "M5 20H19V18H5M19 9H15V3H9V9H5L12 16L19 9Z";
    private const string PicturesGlyph = "M8.5 13.5L11 16.5L14.5 12L19 18H5M21 19V5C21 3.89 20.1 3 19 3H5A2 2 0 0 0 3 5V19A2 2 0 0 0 5 21H19A2 2 0 0 0 21 19Z";
    private const string MusicGlyph = "M12 3V13.55C11.41 13.21 10.73 13 10 13C7.79 13 6 14.79 6 17S7.79 21 10 21 14 19.21 14 17V7H18V3H12Z";
    private const string VideosGlyph = "M17 10.5V7A1 1 0 0 0 16 6H4A1 1 0 0 0 3 7V17A1 1 0 0 0 4 18H16A1 1 0 0 0 17 17V13.5L21 17.5V6.5L17 10.5Z";
    private const string HomeGlyph = "M10 20V14H14V20H19V12H22L12 3L2 12H5V20H10Z";
    private const string PlusGlyph = "M19 13H13V19H11V13H5V11H11V5H13V11H19V13Z";

    /// <summary>Every icon covers this square, so they all scale alike whatever part of it they paint.</summary>
    private static readonly Geometry Frame = Geometry.Parse("M0 0H16V16H0Z");

    private static readonly IBrush FolderBack = Vertical("#EBA91B", "#D4890A");
    private static readonly IBrush FolderPaper = new ImmutableSolidColorBrush(Color.Parse("#FFF9EA"));
    private static readonly IBrush FolderFront = Vertical("#FFDB6E", "#FCC232");

    /// <summary>A darker amber, so a glyph looks pressed into the front of the folder.</summary>
    private static readonly IBrush FolderGlyph = new ImmutableSolidColorBrush(Color.Parse("#A86A00"), 0.85);

    private static readonly IBrush PageFill = Vertical("#FFFFFF", "#EEF0F3");
    private static readonly IBrush PageFoldFill = new ImmutableSolidColorBrush(Color.Parse("#DADDE2"));
    private static readonly IPen PageOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#8B929C")), 1);

    private static readonly IBrush DriveCase = Vertical("#E3E7EC", "#AEB6C0");
    private static readonly IBrush DriveBase = Vertical("#7B8490", "#5F6873");
    private static readonly IBrush DriveLight = new ImmutableSolidColorBrush(Color.Parse("#34D399"));
    private static readonly IPen DriveOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#6F7883")), 0.75);

    private static readonly IBrush ParentArrow = new ImmutableSolidColorBrush(Color.Parse("#8A8F98"));
    private static readonly IBrush NewBadge = new ImmutableSolidColorBrush(Color.Parse("#16A34A"));

    private static readonly Dictionary<string, string> SpecialFolderGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"] = DesktopGlyph,
        ["Documents"] = DocumentsGlyph,
        ["Downloads"] = DownloadsGlyph,
        ["Pictures"] = PicturesGlyph,
        ["Music"] = MusicGlyph,
        ["Videos"] = VideosGlyph,
        ["Home"] = HomeGlyph
    };

    private static readonly Dictionary<string, IImage> SpecialFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The user's own special folders by path, for the glyph a file list shows on them.</summary>
    private static readonly Lazy<Dictionary<string, string>> SpecialFolderPaths = new(FindSpecialFolders);

    private static IImage? _folder;
    private static IImage? _file;
    private static IImage? _drive;
    private static IImage? _parent;
    private static IImage? _newFolder;

    /// <summary>A folder.</summary>
    public static IImage Folder => _folder ??= BuildFolder(glyph: null);

    /// <summary>A file of any kind.</summary>
    public static IImage File => _file ??= Compose(
        new GeometryDrawing { Geometry = Geometry.Parse(PagePath), Brush = PageFill, Pen = PageOutline },
        new GeometryDrawing { Geometry = Geometry.Parse(PageFoldPath), Brush = PageFoldFill, Pen = PageOutline });

    /// <summary>A drive or volume.</summary>
    public static IImage Drive => _drive ??= Compose(
        new GeometryDrawing { Geometry = Geometry.Parse(DriveCasePath), Brush = DriveCase, Pen = DriveOutline },
        new GeometryDrawing { Geometry = Geometry.Parse(DriveBasePath), Brush = DriveBase },
        new GeometryDrawing { Geometry = Geometry.Parse(DriveLightPath), Brush = DriveLight });

    /// <summary>The ".." row that leads to the parent folder.</summary>
    public static IImage Parent => _parent ??= Compose(Glyph(EntryIcons.ParentPath, ParentArrow, size: 16, centerX: 8, centerY: 8));

    /// <summary>A folder with a plus on it, for the command that creates one.</summary>
    public static IImage NewFolder => _newFolder ??= Compose(
        FolderDrawings(glyph: null)
            .Append(new GeometryDrawing { Geometry = Geometry.Parse("M12 8.5A3.5 3.5 0 1 1 11.99 8.5Z"), Brush = NewBadge })
            .Append(Glyph(PlusGlyph, Brushes.White, size: 6.5, centerX: 12, centerY: 12))
            .ToArray());

    /// <summary>The icon of an entry in a file list: the user's special folders carry their glyph there too.</summary>
    public static IImage ForEntry(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.EntryType == FileSystemEntryType.Directory && SpecialFolderOf(entry.FullPath) is { } special
            ? ForFolder(special)
            : ForEntryType(entry.EntryType);
    }

    /// <summary>The icon of a kind of entry.</summary>
    public static IImage ForEntryType(FileSystemEntryType entryType) => entryType switch
    {
        FileSystemEntryType.Directory => Folder,
        FileSystemEntryType.Drive => Drive,
        FileSystemEntryType.ParentDirectory => Parent,
        _ => File
    };

    /// <summary>
    /// A folder, marked when it is one of the folders the system gives every user ("Desktop", "Documents",
    /// "Downloads", "Pictures", "Music", "Videos", "Home") with a glyph that says which, as Finder and Explorer do.
    /// The names are the icon keys the default favorites are saved with; anything else is a plain folder.
    /// </summary>
    public static IImage ForFolder(string? specialFolder)
    {
        if (specialFolder is null || !SpecialFolderGlyphs.TryGetValue(specialFolder, out var glyph))
        {
            return Folder;
        }

        lock (SpecialFolders)
        {
            if (!SpecialFolders.TryGetValue(specialFolder, out var image))
            {
                image = BuildFolder(glyph);
                SpecialFolders[specialFolder] = image;
            }

            return image;
        }
    }

    /// <summary>Which of the user's special folders <paramref name="path"/> is, or null.</summary>
    internal static string? SpecialFolderOf(string path)
    {
        if (string.IsNullOrEmpty(path) || AnyPath.IsRemote(path))
        {
            return null;
        }

        return SpecialFolderPaths.Value.TryGetValue(Path.TrimEndingDirectorySeparator(path), out var key) ? key : null;
    }

    private static Dictionary<string, string> FindSpecialFolders()
    {
        var folders = new Dictionary<string, string>(PathUtils.PathComparer);

        // Home first: a desktop configured as the home folder itself (XDG allows it) must not take its glyph.
        Add("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Downloads", SettingsService.GetDownloadsDirectory());
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        return folders;

        void Add(string key, string? path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                folders.TryAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), key);
            }
        }
    }

    private static IImage BuildFolder(string? glyph) => Compose(FolderDrawings(glyph).ToArray());

    private static IEnumerable<Drawing> FolderDrawings(string? glyph)
    {
        yield return new GeometryDrawing { Geometry = Geometry.Parse(FolderBackPath), Brush = FolderBack };
        yield return new GeometryDrawing { Geometry = Geometry.Parse(FolderPaperPath), Brush = FolderPaper };
        yield return new GeometryDrawing { Geometry = Geometry.Parse(FolderFrontPath), Brush = FolderFront };

        if (glyph is not null)
        {
            // Centred on the front, which spans y 5.5 to 14.
            yield return Glyph(glyph, FolderGlyph, size: 6.5, centerX: 8, centerY: 9.9);
        }
    }

    /// <summary>A 24-unit Material Design glyph scaled to <paramref name="size"/> units and centred on a point.</summary>
    private static Drawing Glyph(string path, IBrush brush, double size, double centerX, double centerY)
    {
        var scale = size / 24;
        return new DrawingGroup
        {
            Transform = new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(centerX - 12 * scale, centerY - 12 * scale)),
            Children = { new GeometryDrawing { Geometry = Geometry.Parse(path), Brush = brush } }
        };
    }

    private static IImage Compose(params Drawing[] drawings)
    {
        var group = new DrawingGroup();

        // Transparent, but it gives every icon the same 16-unit bounds and therefore the same scale.
        group.Children.Add(new GeometryDrawing { Geometry = Frame, Brush = Brushes.Transparent });

        foreach (var drawing in drawings)
        {
            group.Children.Add(drawing);
        }

        return new DrawingImage(group);
    }

    private static IBrush Vertical(string top, string bottom) => new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.Parse(top)), new ImmutableGradientStop(1, Color.Parse(bottom))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
}

/// <summary>Converts a <see cref="FileSystemEntry"/>, or just its <see cref="FileSystemEntryType"/>, to its shared colour icon.</summary>
public sealed class EntryImageConverter : IValueConverter
{
    /// <summary>Gets the shared instance.</summary>
    public static EntryImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            FileSystemEntry entry => EntryImages.ForEntry(entry),
            FileSystemEntryType entryType => EntryImages.ForEntryType(entryType),
            _ => EntryImages.File
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Converts a favorite's icon key to its folder icon.</summary>
public sealed class FavoriteImageConverter : IValueConverter
{
    /// <summary>Gets the shared instance.</summary>
    public static FavoriteImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return EntryImages.ForFolder(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
