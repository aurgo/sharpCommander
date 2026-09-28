using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Utilities;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>The colour icons of the file lists and the favorites.</summary>
public class EntryImagesTests
{
    private static readonly string[] SpecialFolders = ["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "Home"];

    /// <summary>
    /// The special folders that are a place of their own on this system. On Linux, .NET gives the home folder itself
    /// as "Documents", and a folder that is the home folder is shown as home; a folder that does not exist (a build
    /// server has no Desktop) comes back empty and is left out.
    /// </summary>
    private static IEnumerable<(string Key, string Path)> DistinctSpecialFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (key, folder) in new[]
                 {
                     ("Desktop", Environment.SpecialFolder.DesktopDirectory),
                     ("Documents", Environment.SpecialFolder.MyDocuments),
                     ("Pictures", Environment.SpecialFolder.MyPictures),
                     ("Music", Environment.SpecialFolder.MyMusic),
                     ("Videos", Environment.SpecialFolder.MyVideos)
                 })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(home), StringComparison.Ordinal))
            {
                yield return (key, path);
            }
        }
    }

    /// <summary>One special folder to test with: the first of its own, or the home folder, which always exists.</summary>
    private static (string Key, string Path) AnySpecialFolder() =>
        DistinctSpecialFolders().Cast<(string Key, string Path)?>().FirstOrDefault()
        ?? ("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static FileSystemEntry Entry(string path, FileSystemEntryType type) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        EntryType = type
    };

    [AvaloniaFact]
    public void EachKindOfEntryHasItsOwnSharedIcon()
    {
        var icons = Enum.GetValues<FileSystemEntryType>().Select(EntryImages.ForEntryType).ToList();

        Assert.Equal(icons.Count, icons.Distinct().Count());

        // Built once: a list of ten thousand rows shares four images rather than drawing ten thousand.
        Assert.Same(EntryImages.ForEntryType(FileSystemEntryType.Directory), EntryImages.Folder);
        Assert.Same(EntryImages.Folder, EntryImages.Folder);
    }

    [AvaloniaFact]
    public void EveryIconSharesTheSameFrame()
    {
        var icons = new List<IImage> { EntryImages.Folder, EntryImages.File, EntryImages.Drive, EntryImages.Parent, EntryImages.NewFolder };
        icons.AddRange(SpecialFolders.Select(EntryImages.ForFolder));

        // A common frame is what makes them line up: each is scaled from 16 units, whatever part it paints.
        Assert.All(icons, icon => Assert.Equal(new Size(16, 16), icon.Size));
    }

    [AvaloniaFact]
    public void TheSystemFoldersCarryTheirOwnGlyph()
    {
        var icons = SpecialFolders.Select(EntryImages.ForFolder).ToList();

        Assert.DoesNotContain(EntryImages.Folder, icons);
        Assert.Equal(icons.Count, icons.Distinct().Count());
        Assert.Same(EntryImages.ForFolder("Downloads"), EntryImages.ForFolder("downloads"));
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Projects")]
    public void AnyOtherFolderIsAPlainFolder(string? key)
    {
        Assert.Same(EntryImages.Folder, EntryImages.ForFolder(key));
    }

    [AvaloniaFact]
    public void TheUsersOwnFoldersAreRecognisedInTheLists()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var (key, path) in DistinctSpecialFolders())
        {
            Assert.Equal(key, EntryImages.SpecialFolderOf(path));
            Assert.Equal(key, EntryImages.SpecialFolderOf(path + Path.DirectorySeparatorChar));
            Assert.Same(EntryImages.ForFolder(key), EntryImages.ForEntry(Entry(path, FileSystemEntryType.Directory)));
        }

        Assert.Equal("Home", EntryImages.SpecialFolderOf(home));
    }

    [AvaloniaFact]
    public void OnlyFoldersAtThoseExactPlacesGetTheGlyph()
    {
        var documents = AnySpecialFolder().Path;

        Assert.Null(EntryImages.SpecialFolderOf(Path.Combine(documents, "Documents")));
        Assert.Null(EntryImages.SpecialFolderOf("sftp://ana@example.com:22" + documents.Replace('\\', '/')));
        Assert.Same(EntryImages.Folder, EntryImages.ForEntry(Entry(Path.Combine(documents, "Documents"), FileSystemEntryType.Directory)));

        // A file that happens to sit where a special folder would is still a file.
        Assert.Same(EntryImages.File, EntryImages.ForEntry(Entry(documents, FileSystemEntryType.File)));
    }

    [AvaloniaFact]
    public void TheConverterTakesAnEntryOrJustItsKind()
    {
        var converter = EntryImageConverter.Instance;
        var (key, special) = AnySpecialFolder();

        Assert.Same(EntryImages.Drive, converter.Convert(FileSystemEntryType.Drive, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(EntryImages.ForFolder(key), converter.Convert(Entry(special, FileSystemEntryType.Directory), typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(EntryImages.File, converter.Convert(null, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
