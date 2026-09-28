using Avalonia.Media;

namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// Single-colour icon geometries for menus, buttons and the favorites star, parsed once and shared so no binding
/// allocates a geometry. The colour icons of files, folders and drives are in <see cref="EntryImages"/>.
/// </summary>
public static class EntryIcons
{
    /// <summary>Path data of the folder icon.</summary>
    public const string FolderPath = "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2Z";

    /// <summary>Path data of the parent directory (back arrow) icon.</summary>
    public const string ParentPath = "M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2Z";

    /// <summary>Path data of the filled star (favorite) icon.</summary>
    public const string StarFilledPath = "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27Z";

    /// <summary>Path data of the outlined star (not a favorite) icon.</summary>
    public const string StarOutlinePath = "M12 15.39l-3.76 2.27.99-4.28-3.32-2.88 4.38-.37L12 6.09l1.71 4.04 4.38.37-3.32 2.88.99 4.28M22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.64-7.03L22 9.24Z";

    /// <summary>Path data of a small close cross.</summary>
    public const string ClosePath = "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 19 6.41Z";

    /// <summary>Path data of the treemap icon of SpaceAnalyzer: blocks laid out like the ones in its own icon.</summary>
    public const string TreemapPath =
        "M4 2h6a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2Z" +
        "M16 2h4a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2Z" +
        "M15.5 14a1.5 1.5 0 0 1 1.5 1.5v5a1.5 1.5 0 0 1-1.5 1.5a1.5 1.5 0 0 1-1.5-1.5v-5a1.5 1.5 0 0 1 1.5-1.5Z" +
        "M20.5 14a1.5 1.5 0 0 1 1.5 1.5v5a1.5 1.5 0 0 1-1.5 1.5a1.5 1.5 0 0 1-1.5-1.5v-5a1.5 1.5 0 0 1 1.5-1.5Z";

    private static Geometry? _folder;
    private static Geometry? _parent;
    private static Geometry? _starFilled;
    private static Geometry? _starOutline;
    private static Geometry? _close;
    private static Geometry? _treemap;

    /// <summary>Gets the folder icon geometry.</summary>
    public static Geometry Folder => _folder ??= Geometry.Parse(FolderPath);

    /// <summary>Gets the parent directory icon geometry.</summary>
    public static Geometry Parent => _parent ??= Geometry.Parse(ParentPath);

    /// <summary>Gets the filled star geometry.</summary>
    public static Geometry StarFilled => _starFilled ??= Geometry.Parse(StarFilledPath);

    /// <summary>Gets the outlined star geometry.</summary>
    public static Geometry StarOutline => _starOutline ??= Geometry.Parse(StarOutlinePath);

    /// <summary>Gets the close cross geometry.</summary>
    public static Geometry Close => _close ??= Geometry.Parse(ClosePath);

    /// <summary>Gets the treemap (SpaceAnalyzer) geometry.</summary>
    public static Geometry Treemap => _treemap ??= Geometry.Parse(TreemapPath);

}
