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
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal("Documents", EntryImages.SpecialFolderOf(documents));
        Assert.Equal("Documents", EntryImages.SpecialFolderOf(documents + Path.DirectorySeparatorChar));
        Assert.Equal("Home", EntryImages.SpecialFolderOf(home));
        Assert.Same(EntryImages.ForFolder("Documents"), EntryImages.ForEntry(Entry(documents, FileSystemEntryType.Directory)));
    }

    [AvaloniaFact]
    public void OnlyFoldersAtThoseExactPlacesGetTheGlyph()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

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
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.Same(EntryImages.Drive, converter.Convert(FileSystemEntryType.Drive, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(EntryImages.ForFolder("Documents"), converter.Convert(Entry(documents, FileSystemEntryType.Directory), typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(EntryImages.File, converter.Convert(null, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
