using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The real clipboard service (M10): the internal list is the fallback without a system clipboard, and with one
/// the service recognizes its own content until another application replaces it.
/// </summary>
public class ClipboardServiceTests
{
    private static FileSystemEntry Entry(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        EntryType = Directory.Exists(path) ? FileSystemEntryType.Directory : FileSystemEntryType.File
    };

    [AvaloniaFact]
    public async Task WithoutASystemClipboard_TheInternalListIsUsed()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.Dir("b");
        var service = new ClipboardService(() => null);
        var parent = new FileSystemEntry { Name = "..", FullPath = dir.Path, EntryType = FileSystemEntryType.ParentDirectory };

        await service.CopyAsync([Entry(a), parent, Entry(b), Entry(a)]);
        Assert.Equal([a, b], await service.GetPathsAsync());
        Assert.False(service.IsCutMode);

        await service.CutAsync([Entry(b)]);
        Assert.Equal([b], await service.GetPathsAsync());
        Assert.True(service.IsCutMode);

        await service.ClearAsync();
        Assert.Empty(await service.GetPathsAsync());
        Assert.False(service.IsCutMode);

        await service.CutAsync([]);
        Assert.False(service.IsCutMode);
    }

    [AvaloniaFact]
    public async Task WithAWindow_OwnContentIsRecognizedUntilAnotherApplicationReplacesIt()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var window = new Window();
        window.Show();
        try
        {
            var service = new ClipboardService(() => window);

            await service.CutAsync([Entry(a)]);
            Assert.Equal([a], await service.GetPathsAsync());
            Assert.True(service.IsCutMode);

            await window.Clipboard!.SetTextAsync("pasted from somewhere else");

            Assert.Empty(await service.GetPathsAsync());
            Assert.False(service.IsCutMode);
        }
        finally
        {
            window.Close();
        }
    }
}
