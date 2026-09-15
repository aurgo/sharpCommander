using Avalonia.Headless.XUnit;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The operations service against a real file system in a temp directory, with scripted dialogs and trash.
/// Headless facts run on the UI thread so the service's dispatcher updates are processed.
/// </summary>
public class FileOperationsServiceTests
{
    private static readonly FileSystemService FileSystem = new();

    private static FileSystemEntry Entry(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        EntryType = Directory.Exists(path) ? FileSystemEntryType.Directory : FileSystemEntryType.File,
        Attributes = Directory.Exists(path) ? FileAttributes.Directory : FileAttributes.Normal
    };

    private static (FileOperationsService Service, FakeDialogService Dialogs, FakeTrashService Trash) Create(string? trashDirectory = null)
    {
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService(trashDirectory);
        return (new FileOperationsService(FileSystem, dialogs, trash), dialogs, trash);
    }

    // ---- C1: nothing is ever deleted by a move onto itself ------------------------------------------------

    [AvaloniaFact]
    public async Task Move_ToItsOwnFolder_KeepsTheFileAndReportsTheError()
    {
        using var dir = new TempDir();
        var file = dir.File("important.txt", "keep me");
        var (service, dialogs, _) = Create();

        var result = await service.MoveAsync([file], dir.Path);

        Assert.True(File.Exists(file));
        Assert.Equal("keep me", File.ReadAllText(file));
        Assert.Equal(1, result.Failed);
        Assert.Contains("same", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(dialogs.Calls, call => call.StartsWith("conflict:", StringComparison.Ordinal));
        Assert.DoesNotContain(dialogs.Calls, call => call.StartsWith("delete:", StringComparison.Ordinal));
        Assert.Single(dialogs.ReportedErrors);
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public async Task Move_DirectoryIntoItself_IsRefusedUpFront()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        var file = dir.File(Path.Combine("docs", "a.txt"));
        var sub = dir.Dir(Path.Combine("docs", "sub"));
        var (service, _, _) = Create();

        var result = await service.MoveAsync([docs], sub);

        Assert.True(File.Exists(file));
        Assert.Equal(1, result.Failed);
        Assert.Contains("into itself", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.False(result.Started);
    }

    [AvaloniaFact]
    public async Task Move_OtherItemsStillRun_WhenOneIsRejected()
    {
        using var dir = new TempDir();
        var stays = dir.File("stays.txt");
        var moves = dir.File(Path.Combine("src", "moves.txt"));
        var (service, _, _) = Create();

        var result = await service.MoveAsync([stays, moves], dir.Path);

        Assert.True(File.Exists(stays));
        Assert.True(File.Exists(Path.Combine(dir.Path, "moves.txt")));
        Assert.False(File.Exists(moves));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }

    // ---- C2: conflicts ask, folders merge --------------------------------------------------------------

    [AvaloniaFact]
    public async Task Move_OntoExistingFolder_SkipKeepsDestinationFiles()
    {
        using var dir = new TempDir();
        var source = dir.Dir(Path.Combine("src", "docs"));
        dir.File(Path.Combine("src", "docs", "a.txt"), "new a");
        dir.File(Path.Combine("src", "docs", "b.txt"), "new b");
        var destination = dir.Dir("dst");
        dir.File(Path.Combine("dst", "docs", "a.txt"), "old a");
        dir.File(Path.Combine("dst", "docs", "c.txt"), "old c");
        var (service, dialogs, _) = Create();
        dialogs.ConflictAnswer = new ConflictResolution(ConflictAction.Skip);

        var result = await service.MoveAsync([source], destination);

        Assert.Equal("old a", File.ReadAllText(Path.Combine(destination, "docs", "a.txt")));
        Assert.Equal("new b", File.ReadAllText(Path.Combine(destination, "docs", "b.txt")));
        Assert.Equal("old c", File.ReadAllText(Path.Combine(destination, "docs", "c.txt")));
        Assert.True(File.Exists(Path.Combine(source, "a.txt")), "the skipped file stays at the source");
        Assert.False(File.Exists(Path.Combine(source, "b.txt")));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Single(dialogs.Calls, call => call.StartsWith("conflict:", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Copy_OverwriteApplyToAll_AsksOnlyOnce()
    {
        using var dir = new TempDir();
        var sources = new[] { dir.File("a.txt", "new"), dir.File("b.txt", "new"), dir.File("c.txt", "new") };
        var destination = dir.Dir("dst");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            dir.File(Path.Combine("dst", name), "old");
        }

        var (service, dialogs, _) = Create();
        dialogs.ConflictAnswer = new ConflictResolution(ConflictAction.Overwrite, ApplyToAll: true);

        var result = await service.CopyAsync(sources, destination);

        Assert.Single(dialogs.Calls, call => call.StartsWith("conflict:", StringComparison.Ordinal));
        Assert.All(new[] { "a.txt", "b.txt", "c.txt" }, name => Assert.Equal("new", File.ReadAllText(Path.Combine(destination, name))));
        Assert.Equal(3, result.Succeeded);
        Assert.Empty(dialogs.ReportedErrors);
    }

    [AvaloniaFact]
    public async Task Copy_CancelOnConflict_StopsTheBatchWithoutErrorDialog()
    {
        using var dir = new TempDir();
        var sources = new[] { dir.File("a.txt", "new"), dir.File("b.txt", "new") };
        var destination = dir.Dir("dst");
        dir.File(Path.Combine("dst", "a.txt"), "old");
        dir.File(Path.Combine("dst", "b.txt"), "old");
        var (service, dialogs, _) = Create();
        dialogs.ConflictAnswer = new ConflictResolution(ConflictAction.Cancel);

        var result = await service.CopyAsync(sources, destination);

        Assert.True(result.Cancelled);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "b.txt")));
        Assert.Single(dialogs.Calls, call => call.StartsWith("conflict:", StringComparison.Ordinal));
        Assert.DoesNotContain(dialogs.Calls, call => call.StartsWith("operationerrors:", StringComparison.Ordinal));
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public async Task Copy_RenameAnswer_UsesTheTypedNameOnce()
    {
        using var dir = new TempDir();
        var source = dir.File("a.txt", "new");
        var destination = dir.Dir("dst");
        dir.File(Path.Combine("dst", "a.txt"), "old");
        var (service, dialogs, _) = Create();
        dialogs.ConflictAnswer = new ConflictResolution(ConflictAction.Rename, NewName: "a (copy).txt");

        var result = await service.CopyAsync([source], destination);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "a (copy).txt")));
    }

    // ---- C3: delete confirmation, trash by default --------------------------------------------------------

    [AvaloniaFact]
    public async Task Delete_TrashAnswer_UsesTheTrashService()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.Dir("b");
        var (service, dialogs, trash) = Create(Path.Combine(dir.Path, "trash"));
        dialogs.DeleteAnswer = DeleteChoice.Trash;

        var result = await service.DeleteAsync([Entry(a), Entry(b)], permanentRequested: false);

        Assert.Contains("delete:2:trash:default", dialogs.Calls);
        Assert.Equal(new[] { a, b }, trash.Trashed);
        Assert.False(File.Exists(a));
        Assert.False(Directory.Exists(b));
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(FileOperationKind.Trash, result.Kind);
    }

    [AvaloniaFact]
    public async Task Delete_PermanentAnswer_DeletesDirectly()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var nested = dir.File(Path.Combine("b", "nested.txt"));
        var (service, dialogs, trash) = Create();
        dialogs.DeleteAnswer = DeleteChoice.Permanent;

        var result = await service.DeleteAsync([Entry(a), Entry(Path.GetDirectoryName(nested)!)], permanentRequested: true);

        Assert.Contains("delete:2:trash:permanent", dialogs.Calls);
        Assert.Empty(trash.Trashed);
        Assert.False(File.Exists(a));
        Assert.False(Directory.Exists(Path.GetDirectoryName(nested)));
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(FileOperationKind.Delete, result.Kind);
    }

    [AvaloniaFact]
    public async Task Delete_CancelAnswer_DoesNothing()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var (service, dialogs, trash) = Create();
        dialogs.DeleteAnswer = DeleteChoice.Cancel;

        var result = await service.DeleteAsync([Entry(a)], permanentRequested: false);

        Assert.True(File.Exists(a));
        Assert.True(result.Cancelled);
        Assert.False(result.Started);
        Assert.Empty(trash.Trashed);
        Assert.DoesNotContain(dialogs.Calls, call => call.StartsWith("operationerrors:", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Delete_WithoutTrashSupport_OffersPermanentOnly()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var (service, dialogs, trash) = Create();
        trash.IsSupported = false;
        dialogs.DeleteAnswer = DeleteChoice.Permanent;

        await service.DeleteAsync([Entry(a)], permanentRequested: false);

        Assert.Contains("delete:1:notrash:default", dialogs.Calls);
        Assert.False(File.Exists(a));
    }

    [AvaloniaFact]
    public async Task Delete_TrashFailure_FallsBackToPermanentAfterConfirmation()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.File("b.txt");
        var (service, dialogs, trash) = Create();
        dialogs.DeleteAnswer = DeleteChoice.Trash;
        trash.FailWith = path => new IOException("The volume has no trash.");

        dialogs.ConfirmAnswer = true;
        var result = await service.DeleteAsync([Entry(a)], permanentRequested: false);
        Assert.Contains("confirm:Delete permanently?", dialogs.Calls);
        Assert.False(File.Exists(a));
        Assert.Equal(1, result.Succeeded);

        dialogs.ConfirmAnswer = false;
        result = await service.DeleteAsync([Entry(b)], permanentRequested: false);
        Assert.True(File.Exists(b));
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    // ---- M1: rename ---------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Rename_ValidName_RenamesAndReturnsNewPath()
    {
        using var dir = new TempDir();
        var file = dir.File("old.txt", "content");
        var (service, dialogs, _) = Create();
        dialogs.InputAnswer = "new.txt";

        var newPath = await service.RenameAsync(Entry(file));

        Assert.Equal(Path.Combine(dir.Path, "new.txt"), newPath);
        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(file));
    }

    [AvaloniaFact]
    public async Task Rename_InvalidNameOrExistingTarget_IsRejectedByValidation()
    {
        using var dir = new TempDir();
        var file = dir.File("old.txt");
        dir.File("taken.txt");
        var (service, dialogs, _) = Create();

        dialogs.InputAnswer = "bad/name";
        Assert.Null(await service.RenameAsync(Entry(file)));

        dialogs.InputAnswer = "..";
        Assert.Null(await service.RenameAsync(Entry(file)));

        dialogs.InputAnswer = "taken.txt";
        Assert.Null(await service.RenameAsync(Entry(file)));

        Assert.True(File.Exists(file));
        Assert.Empty(dialogs.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Rename_SameNameOrCancel_IsANoOp()
    {
        using var dir = new TempDir();
        var file = dir.File("same.txt");
        var (service, dialogs, _) = Create();

        dialogs.InputAnswer = "same.txt";
        Assert.Null(await service.RenameAsync(Entry(file)));

        dialogs.InputAnswer = null;
        Assert.Null(await service.RenameAsync(Entry(file)));

        Assert.True(File.Exists(file));
    }

    // ---- new folder ---------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CreateDirectory_ValidName_CreatesAndReturnsPath()
    {
        using var dir = new TempDir();
        var (service, dialogs, _) = Create();
        dialogs.InputAnswer = "Projects";

        var created = await service.CreateDirectoryAsync(dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "Projects"), created);
        Assert.True(Directory.Exists(created));
    }

    [AvaloniaFact]
    public async Task CreateDirectory_ExistingOrInvalidName_IsRejected()
    {
        using var dir = new TempDir();
        dir.Dir("Projects");
        var (service, dialogs, _) = Create();

        dialogs.InputAnswer = "Projects";
        Assert.Null(await service.CreateDirectoryAsync(dir.Path));

        dialogs.InputAnswer = "a/b";
        Assert.Null(await service.CreateDirectoryAsync(dir.Path));

        Assert.Null(await service.CreateDirectoryAsync(string.Empty));
        Assert.Contains("error:New Folder", dialogs.Calls);
    }

    // ---- H1/H3: errors are collected and shown once -----------------------------------------------------

    [AvaloniaFact]
    public async Task Copy_MissingSource_IsReportedThroughTheErrorsDialog()
    {
        using var dir = new TempDir();
        var destination = dir.Dir("dst");
        var missing = Path.Combine(dir.Path, "missing.txt");
        var existing = dir.File("existing.txt");
        var (service, dialogs, _) = Create();

        var result = await service.CopyAsync([missing, existing], destination);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
        Assert.Single(dialogs.ReportedErrors);
        Assert.Equal(missing, dialogs.ReportedErrors[0].Path);
        Assert.Single(dialogs.Calls, call => call.StartsWith("operationerrors:", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(destination, "existing.txt")));
    }

    [AvaloniaFact]
    public async Task Copy_ToMissingDestination_IsRejectedWithAnError()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt");
        var (service, dialogs, _) = Create();

        var result = await service.CopyAsync([file], Path.Combine(dir.Path, "nowhere"));

        Assert.True(result.Rejected);
        Assert.Contains("error:Copy", dialogs.Calls);
    }

    // ---- one batch at a time, progress and cancellation --------------------------------------------------

    [AvaloniaFact]
    public async Task SecondBatch_WhileOneIsRunning_IsRejected()
    {
        using var dir = new TempDir();
        var source = dir.File("a.txt", "new");
        var destination = dir.Dir("dst");
        dir.File(Path.Combine("dst", "a.txt"), "old");
        var (service, dialogs, _) = Create();

        var gate = new TaskCompletionSource<ConflictResolution>();
        dialogs.ConflictHandler = (_, _) => gate.Task;

        var first = service.CopyAsync([source], destination);
        var second = await service.CopyAsync([source], destination);

        Assert.True(second.Rejected);
        Assert.Contains("error:Operation in progress", dialogs.Calls);
        Assert.True(service.IsRunning);

        gate.SetResult(new ConflictResolution(ConflictAction.Skip));
        var result = await first;
        Assert.Equal(1, result.Skipped);
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public async Task Cancel_DuringConflict_StopsTheBatch()
    {
        using var dir = new TempDir();
        var source = dir.File("a.txt", "new");
        var destination = dir.Dir("dst");
        dir.File(Path.Combine("dst", "a.txt"), "old");
        var (service, dialogs, _) = Create();
        dialogs.ConflictHandler = (_, _) =>
        {
            service.Cancel();
            return Task.FromResult(new ConflictResolution(ConflictAction.Overwrite));
        };

        var result = await service.CopyAsync([source], destination);

        Assert.True(result.Cancelled);
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public async Task Progress_CoversTheWholeBatch()
    {
        using var dir = new TempDir();
        var a = dir.File("a.bin", new string('a', 3000));
        var b = dir.File("b.bin", new string('b', 5000));
        var destination = dir.Dir("dst");
        var (service, _, _) = Create();

        var totals = new List<long>();
        var maxProcessed = 0L;
        var sawRunning = false;
        service.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(service.TotalBytes):
                    totals.Add(service.TotalBytes);
                    break;
                case nameof(service.ProcessedBytes):
                    maxProcessed = Math.Max(maxProcessed, service.ProcessedBytes);
                    break;
                case nameof(service.IsRunning):
                    sawRunning |= service.IsRunning;
                    break;
            }
        };

        var result = await service.CopyAsync([a, b], destination);

        Assert.Equal(2, result.Succeeded);
        Assert.True(sawRunning);
        Assert.Contains(8000, totals);
        Assert.Equal(8000, maxProcessed);
        Assert.False(service.IsRunning);
        Assert.Equal(0, service.Percent);
        Assert.Equal(string.Empty, service.CurrentFile);
    }

    [AvaloniaFact]
    public async Task OperationCompleted_IsRaisedForStartedBatches()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt");
        var destination = dir.Dir("dst");
        var (service, _, _) = Create();
        var completed = new List<FileOperationResult>();
        service.OperationCompleted += (_, result) => completed.Add(result);

        await service.CopyAsync([file], destination);
        await service.CopyAsync([], destination);

        var result = Assert.Single(completed);
        Assert.True(result.Started);
        Assert.Equal(FileOperationKind.Copy, result.Kind);
    }

    // ---- open ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Open_Script_AsksBeforeRunning()
    {
        using var dir = new TempDir();
        var script = dir.File("run.sh", "#!/bin/sh\n");
        var (service, dialogs, _) = Create();
        dialogs.ConfirmAnswer = false;

        var opened = await service.OpenAsync(Entry(script));

        Assert.False(opened);
        Assert.Contains("confirm:Run program?", dialogs.Calls);
    }

    // ---- transfers that cross to a server ------------------------------------------------------------------

    private const string Remote = "sftp://ana@example.com:22";

    private static (FileOperationsService Service, FakeDialogService Dialogs, FakeSftpServer Server) CreateWithServer()
    {
        var server = new FakeSftpServer();
        var connections = new SftpConnections(() => server);
        connections.ConnectAsync(new SftpSite { Host = "example.com", Port = 22, Username = "ana" }, null).GetAwaiter().GetResult();

        var dialogs = new FakeDialogService();
        var router = new RoutingFileSystemService(FileSystem, connections);
        return (new FileOperationsService(router, dialogs, new FakeTrashService()), dialogs, server);
    }

    /// <summary>
    /// Planning used to ask the local disk about every source, and a server address is not a path it knows: an
    /// F5 out of a remote panel failed with "no longer exists" before a single byte was asked for.
    /// </summary>
    [AvaloniaFact]
    public async Task Copy_FromAServerToTheLocalDisk_DownloadsTheFile()
    {
        using var dir = new TempDir();
        var (service, dialogs, server) = CreateWithServer();
        server.AddFile("/home/ana/notes.txt", "hello");

        var result = await service.CopyAsync([$"{Remote}/home/ana/notes.txt"], dir.Path);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dir.Path, "notes.txt")));
        Assert.Empty(dialogs.ReportedErrors);
    }

    [AvaloniaFact]
    public async Task Move_AFolderFromAServer_BringsItDownAndRemovesIt()
    {
        using var dir = new TempDir();
        var (service, _, server) = CreateWithServer();
        server.AddFile("/home/ana/docs/a.txt", "one");
        server.AddFile("/home/ana/docs/b.txt", "two");

        var result = await service.MoveAsync([$"{Remote}/home/ana/docs"], dir.Path);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("one", File.ReadAllText(Path.Combine(dir.Path, "docs", "a.txt")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(dir.Path, "docs", "b.txt")));
        Assert.False(server.HasFile("/home/ana/docs/a.txt"));
    }

    [AvaloniaFact]
    public async Task Copy_ToAServer_UploadsTheFile()
    {
        using var dir = new TempDir();
        var (service, _, server) = CreateWithServer();
        server.AddDirectory("/home/ana");
        var file = dir.File("report.txt", "contents");

        var result = await service.CopyAsync([file], $"{Remote}/home/ana");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("contents", server.ReadFile("/home/ana/report.txt"));
    }

    [AvaloniaFact]
    public async Task Copy_OnTheServerIntoItsOwnFolder_IsRefusedUpFront()
    {
        var (service, _, server) = CreateWithServer();
        server.AddFile("/home/ana/notes.txt", "hello");

        var result = await service.CopyAsync([$"{Remote}/home/ana/notes.txt"], $"{Remote}/home/ana");

        Assert.Equal(1, result.Failed);
        Assert.Contains("same", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("hello", server.ReadFile("/home/ana/notes.txt"));
    }

    [AvaloniaFact]
    public async Task Copy_AServerFolderIntoItself_IsRefusedUpFront()
    {
        var (service, _, server) = CreateWithServer();
        server.AddFile("/home/ana/docs/a.txt", "one");
        server.AddDirectory("/home/ana/docs/sub");

        var result = await service.CopyAsync([$"{Remote}/home/ana/docs"], $"{Remote}/home/ana/docs/sub");

        Assert.Equal(1, result.Failed);
        Assert.Contains("itself", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(server.HasFile("/home/ana/docs/sub/docs/a.txt"));
    }

    [AvaloniaFact]
    public async Task Copy_AFileThatIsGoneFromTheServer_ReportsIt()
    {
        using var dir = new TempDir();
        var (service, _, _) = CreateWithServer();

        var result = await service.CopyAsync([$"{Remote}/home/ana/vanished.txt"], dir.Path);

        Assert.Equal(1, result.Failed);
        Assert.Contains("vanished.txt", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    // ---- audit regression: a whole drive is not a transfer source ----------------------------------------

    [AvaloniaFact]
    public async Task Copy_AVolumeRoot_IsRefusedWithoutTouchingTheDestination()
    {
        // The Computer view auto-selects the first drive, so F5/F6 and drag and drop could hand a volume root
        // to the service. Path.GetFileName of a root is empty, so the target became the destination folder
        // itself and the whole drive merged into it flat, with no confirmation.
        using var dir = new TempDir();
        var destination = dir.Dir("destination");
        var root = Path.GetPathRoot(dir.Path)!;
        var (service, dialogs, _) = Create();

        var result = await service.CopyAsync([root], destination);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Succeeded);
        Assert.Contains("drive", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        Assert.DoesNotContain(dialogs.Calls, call => call.StartsWith("conflict:", StringComparison.Ordinal));
        Assert.False(service.IsRunning);
    }

    [AvaloniaFact]
    public async Task Move_AVolumeRoot_IsRefusedAndDeletesNothing()
    {
        using var dir = new TempDir();
        var destination = dir.Dir("destination");
        var root = Path.GetPathRoot(dir.Path)!;
        var (service, _, _) = Create();

        var result = await service.MoveAsync([root], destination);

        Assert.Equal(1, result.Failed);
        Assert.True(Directory.Exists(root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }
}
