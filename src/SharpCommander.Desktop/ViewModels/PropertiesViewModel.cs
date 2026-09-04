using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Everything the properties window shows about one entry. Folder size and item counts are computed in the
/// background by <see cref="StartAsync"/> and can be cancelled.
/// </summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private CancellationTokenSource? _cts;
    private bool _started;

    /// <summary>Gets the entry described by this view model.</summary>
    public FileSystemEntry Entry { get; }

    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public FileSystemEntryType EntryType => Entry.EntryType;

    /// <summary>Human readable kind: "File (.txt)", "Folder", "Drive (APFS)", with a link note when applicable.</summary>
    public string TypeText { get; }

    /// <summary>Parent folder (the mount path for drives).</summary>
    public string Location { get; }

    public bool IsDirectory => Entry.EntryType is FileSystemEntryType.Directory or FileSystemEntryType.ParentDirectory;
    public bool IsDrive => Entry.EntryType == FileSystemEntryType.Drive;
    public bool IsFile => Entry.EntryType == FileSystemEntryType.File;

    public string CreatedText => FormatDate(Entry.Created);
    public string ModifiedText => FormatDate(Entry.LastModified);
    public string AccessedText => FormatDate(Entry.LastAccessed);

    /// <summary>Comma separated attribute names, or "-" when none apply.</summary>
    public string AttributesText { get; }

    public bool HasUnixPermissions => !string.IsNullOrEmpty(Entry.UnixPermissions);
    public string UnixPermissionsText => Entry.UnixPermissions ?? "-";

    public bool HasDriveInfo => IsDrive && Entry.TotalSize is not null;

    /// <summary>Free and total space plus the file system format, for drives.</summary>
    public string DriveInfoText { get; }

    /// <summary>Formatted size with the exact byte count; "Calculating..." while a folder is being measured.</summary>
    [ObservableProperty]
    private string _sizeText = string.Empty;

    /// <summary>"N files, M folders" for directories.</summary>
    [ObservableProperty]
    private string _contentsText = string.Empty;

    [ObservableProperty]
    private bool _isCalculating;

    public PropertiesViewModel(FileSystemEntry entry, IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fileSystemService);

        Entry = entry;
        _fileSystemService = fileSystemService;

        TypeText = DescribeType(entry);
        Location = IsDrive ? entry.FullPath : Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath;
        AttributesText = DescribeAttributes(entry.Attributes);
        DriveInfoText = DescribeDrive(entry);
        SizeText = IsDirectory ? "Unknown" : FormatSize(entry.Size);
    }

    /// <summary>
    /// Measures a folder: total size through the file system service (reporting a running total) and file and
    /// folder counts. Does nothing for files and drives. Never throws.
    /// </summary>
    public async Task StartAsync()
    {
        if (!IsDirectory || _started)
        {
            return;
        }

        _started = true;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsCalculating = true;
        SizeText = "Calculating...";
        ContentsText = "Calculating...";

        var progress = new Progress<long>(bytes => SizeText = $"{FormatSize(bytes)} (calculating...)");

        try
        {
            var sizeTask = _fileSystemService.GetDirectorySizeAsync(FullPath, progress, cts.Token);
            var countTask = Task.Run(() => CountItems(FullPath, cts.Token), cts.Token);

            var size = await sizeTask;
            var (files, folders) = await countTask;

            SizeText = FormatSize(size);
            ContentsText = $"{Plural(files, "file", "files")}, {Plural(folders, "folder", "folders")}";
        }
        catch (OperationCanceledException)
        {
            SizeText = "Cancelled";
            ContentsText = "Cancelled";
        }
        catch (Exception ex)
        {
            SizeText = "Unavailable";
            ContentsText = $"Error: {ex.Message}";
        }
        finally
        {
            _cts = null;
            IsCalculating = false;
        }
    }

    /// <summary>Stops the folder measurement. Safe to call at any time.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    private static (int Files, int Folders) CountItems(string path, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var files = 0;
        var folders = 0;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                folders++;
            }
            else
            {
                files++;
            }
        }

        return (files, folders);
    }

    private static string FormatSize(long bytes)
    {
        return $"{FileSizeFormatter.Format(bytes)} ({bytes:N0} bytes)";
    }

    private static string FormatDate(DateTime value)
    {
        return value == default || value.Year < 1980 ? "-" : value.ToString("f", CultureInfo.CurrentCulture);
    }

    private static string Plural(int count, string singular, string plural)
    {
        return count == 1 ? $"1 {singular}" : $"{count:N0} {plural}";
    }

    private static string DescribeType(FileSystemEntry entry)
    {
        var kind = entry.EntryType switch
        {
            FileSystemEntryType.Directory or FileSystemEntryType.ParentDirectory => "Folder",
            FileSystemEntryType.Drive => string.IsNullOrEmpty(entry.DriveFormat) ? "Drive" : $"Drive ({entry.DriveFormat})",
            _ => string.IsNullOrEmpty(entry.Extension) ? "File" : $"File ({entry.Extension})"
        };

        return entry.IsSymbolicLink ? $"{kind}, symbolic link" : kind;
    }

    private static string DescribeAttributes(FileAttributes attributes)
    {
        var names = new List<string>();
        Add(FileAttributes.ReadOnly, "Read-only");
        Add(FileAttributes.Hidden, "Hidden");
        Add(FileAttributes.System, "System");
        Add(FileAttributes.Archive, "Archive");
        Add(FileAttributes.Compressed, "Compressed");
        Add(FileAttributes.Encrypted, "Encrypted");
        Add(FileAttributes.Temporary, "Temporary");
        Add(FileAttributes.ReparsePoint, "Reparse point");
        Add(FileAttributes.Offline, "Offline");
        return names.Count == 0 ? "-" : string.Join(", ", names);

        void Add(FileAttributes flag, string name)
        {
            if ((attributes & flag) != 0)
            {
                names.Add(name);
            }
        }
    }

    private static string DescribeDrive(FileSystemEntry entry)
    {
        if (entry.EntryType != FileSystemEntryType.Drive || entry.TotalSize is null)
        {
            return string.Empty;
        }

        var free = entry.FreeSpace is { } value ? FileSizeFormatter.Format(value) : "unknown";
        var format = string.IsNullOrEmpty(entry.DriveFormat) ? string.Empty : $" ({entry.DriveFormat})";
        return $"{free} free of {FileSizeFormatter.Format(entry.TotalSize.Value)}{format}";
    }
}
