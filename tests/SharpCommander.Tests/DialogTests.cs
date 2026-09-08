using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Localization;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Dialog windows and the secondary tools (search, hash, properties, viewer, mass rename), headless.
/// </summary>
public class DialogTests
{
    private static readonly FileSystemService FileSystem = new();

    private static FileSystemEntry DirectoryEntry(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        EntryType = FileSystemEntryType.Directory,
        Attributes = FileAttributes.Directory
    };

    private static FileSystemEntry FileEntry(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        EntryType = FileSystemEntryType.File,
        Size = new FileInfo(path).Length,
        Extension = Path.GetExtension(path)
    };

    /// <summary>Pumps the dispatcher and returns the single dialog of the given type owned by <paramref name="owner"/>.</summary>
    private static T OwnedDialog<T>(Window owner) where T : Window
    {
        Dispatcher.UIThread.RunJobs();
        return owner.OwnedWindows.OfType<T>().Single();
    }

    // ---- DataGrid theme (H2) ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void DataGridTheme_IsRegisteredInApp()
    {
        Assert.True(Application.Current!.TryGetResource(typeof(DataGrid), null, out _), "DataGrid ControlTheme must be registered in App.axaml");
    }

    [AvaloniaFact]
    public void MassRenameWindow_HasStatusColumnAndTemplate()
    {
        using var dir = new TempDir();
        var vm = new MassRenameViewModel(FileSystem, [dir.File("a.txt"), dir.File("b.txt")]);
        var window = new MassRenameWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single();
        Assert.NotNull(grid.Template);
        Assert.Equal(3, grid.Columns.Count);

        // The header is localized, so compare against the catalogue rather than a literal: another test may
        // have switched the language, which is global state.
        Assert.Equal(Strings.Get("MassRenameWindow_Status"), grid.Columns[2].Header);
        window.Close();
    }

    // ---- Mass rename (M2) ----------------------------------------------------------------------------

    [Fact]
    public void MassRename_Preview_FlagsEveryConflictKind()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.File("b.txt");
        dir.File("c.txt"); // exists on disk, not part of the batch

        var vm = new MassRenameViewModel(FileSystem, [a, b]);
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.Unchanged, item.Status));
        Assert.False(vm.CanApply);
        Assert.Equal("0 ok, 2 unchanged, 0 conflicts", vm.SummaryText);

        vm.FileNameMask = "same"; // both become same.txt
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.DuplicateInBatch, item.Status));
        Assert.False(vm.CanApply);

        vm.FileNameMask = "[N]";
        vm.SearchPattern = "a";
        vm.ReplacePattern = "c"; // a.txt -> c.txt, which exists outside the batch
        Assert.Equal(RenameStatus.TargetExists, vm.Items[0].Status);
        Assert.Equal(RenameStatus.Unchanged, vm.Items[1].Status);
        Assert.Equal("0 ok, 1 unchanged, 1 conflicts", vm.SummaryText);
        Assert.False(vm.CanApply);

        vm.SearchPattern = string.Empty;
        vm.FileNameMask = "[N]/x"; // path separator
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.InvalidName, item.Status));
        Assert.False(vm.CanApply);

        vm.FileNameMask = "[N]_[C]";
        vm.CounterPadding = 3;
        Assert.Equal(new[] { "a_001.txt", "b_002.txt" }, vm.Items.Select(item => item.NewName));
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.Ok, item.Status));
        Assert.True(vm.CanApply);
        Assert.Equal("2 ok, 0 unchanged, 0 conflicts", vm.SummaryText);

        vm.FileNameMask = "[N]";
        vm.CaseOption = 2; // A.TXT: a case-only rename of an item of the batch is allowed everywhere
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.Ok, item.Status));
    }

    [Fact]
    public void MassRename_InvalidRegex_IsReportedAndIgnored()
    {
        using var dir = new TempDir();
        var vm = new MassRenameViewModel(FileSystem, [dir.File("a.txt")]) { UseRegex = true, SearchPattern = "(" };

        Assert.StartsWith("Invalid regular expression", vm.StatusText);
        Assert.Equal("a.txt", vm.Items[0].NewName);
    }

    [Fact]
    public async Task MassRename_Apply_RenamesCyclesInTwoPhases()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt", "A");
        var b = dir.File("b.txt", "B");
        var c = dir.File("c.txt", "C");
        var vm = new MassRenameViewModel(FileSystem, [a, b, c]);

        vm.Items[0].NewName = "b.txt";
        vm.Items[1].NewName = "c.txt";
        vm.Items[2].NewName = "a.txt";
        vm.RefreshStatuses();
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.Ok, item.Status));
        Assert.True(vm.CanApply);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.HasRenamed);
        Assert.False(vm.HasFailures);
        Assert.Equal("Renamed 3 items.", vm.StatusText);
        Assert.Equal("A", File.ReadAllText(Path.Combine(dir.Path, "b.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(dir.Path, "c.txt")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(dir.Path, "a.txt")));
        Assert.Equal(3, Directory.GetFiles(dir.Path).Length); // no temporary names left behind
        Assert.Equal(new[] { "b.txt", "c.txt", "a.txt" }, vm.Items.Select(item => item.OldName));
        Assert.All(vm.Items, item => Assert.Equal(RenameStatus.Unchanged, item.Status));
        Assert.False(vm.CanApply);
    }

    [Fact]
    public async Task MassRename_Apply_ReportsPerItemFailures()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt", "A");
        var vm = new MassRenameViewModel(FileSystem, [a]);
        vm.Items[0].NewName = "renamed.txt";
        vm.RefreshStatuses();
        File.Delete(a); // vanishes before apply

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.False(vm.HasRenamed);
        Assert.True(vm.HasFailures);
        Assert.Equal(RenameStatus.Failed, vm.Items[0].Status);
        Assert.Equal("Renamed 0 items, 1 failed.", vm.StatusText);
        Assert.False(vm.CanApply);
    }

    // ---- Search (M3, H9) -------------------------------------------------------------------------------

    [Fact]
    public void Search_GlobToRegex_MatchesLikeAShell()
    {
        Assert.Equal(@"^.*\.txt$", SearchViewModel.GlobToRegex("*.txt"));
        Assert.Equal(@"^report_..\.pdf$", SearchViewModel.GlobToRegex("report_??.pdf"));

        var txt = SearchViewModel.TryCreateNameMatcher("*.txt", useRegex: false, out var error)!;
        Assert.Null(error);
        Assert.Matches(txt, "notes.txt");
        Assert.Matches(txt, "NOTES.TXT");
        Assert.DoesNotMatch(txt, "notes.txt.bak");

        var question = SearchViewModel.TryCreateNameMatcher("a?b*", useRegex: false, out _)!;
        Assert.Matches(question, "axb123");
        Assert.DoesNotMatch(question, "ab");

        var regex = SearchViewModel.TryCreateNameMatcher(@"^\d+\.log$", useRegex: true, out _)!;
        Assert.Matches(regex, "42.log");
        Assert.DoesNotMatch(regex, "x42.log");

        Assert.Null(SearchViewModel.TryCreateNameMatcher("(", useRegex: true, out var invalid));
        Assert.NotNull(invalid);
    }

    [AvaloniaFact]
    public async Task Search_InvalidRegex_IsReportedWithoutSearching()
    {
        using var dir = new TempDir();
        dir.File("x.txt");
        var vm = new SearchViewModel(FileSystem, dir.Path) { UseRegex = true, FileNamePattern = "(" };

        await vm.StartSearchCommand.ExecuteAsync(null);

        Assert.StartsWith("Invalid pattern", vm.StatusText);
        Assert.False(vm.IsSearching);
        Assert.Empty(vm.Results);
    }

    [AvaloniaFact]
    public async Task Search_FindsByNameAndByContent()
    {
        using var dir = new TempDir();
        dir.File("notes.txt", "hello world");
        dir.File("sub/other.txt", "nothing here");
        dir.File("sub/readme.md", "hello again");
        dir.File("binary.txt", "hello\0world"); // NUL byte: never matched by content

        var vm = new SearchViewModel(FileSystem, dir.Path) { FileNamePattern = "*.txt" };
        await vm.StartSearchCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "binary.txt", "notes.txt", "other.txt" }, vm.Results.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.StartsWith("Search completed", vm.StatusText);
        Assert.Contains("3 items", vm.StatusText);

        vm.ContentPattern = "WORLD";
        await vm.StartSearchCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "notes.txt" }, vm.Results.Select(r => r.Name));

        vm.FileNamePattern = "*";
        vm.ContentPattern = "hello";
        await vm.StartSearchCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "notes.txt", "readme.md" }, vm.Results.Select(r => r.Name).Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task Search_CanBeCancelled()
    {
        using var dir = new TempDir();
        for (var i = 0; i < 300; i++)
        {
            dir.File($"d{i}/f{i}.txt");
        }

        var vm = new SearchViewModel(FileSystem, dir.Path);
        var run = vm.StartSearchCommand.ExecuteAsync(null);
        Assert.True(vm.IsSearching);
        Assert.True(vm.CancelSearchCommand.CanExecute(null));

        vm.CancelSearchCommand.Execute(null);
        await run;

        Assert.False(vm.IsSearching);
        Assert.StartsWith("Search cancelled", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Search_ActivatingAResult_ClosesWithThatEntry()
    {
        using var dir = new TempDir();
        var file = dir.File("target.txt");
        var vm = new SearchViewModel(FileSystem, dir.Path) { FileNamePattern = "target*" };
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.StartSearchCommand.ExecuteAsync(null);
        Assert.False(vm.ActivateResultCommand.CanExecute(null));

        vm.SelectedResult = vm.Results.Single();
        vm.ActivateResultCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(file, vm.ActivatedResult!.FullPath);
    }

    // ---- Hash (L4) -------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Hash_ComputesAllFourAlgorithms()
    {
        using var dir = new TempDir();
        var vm = new HashViewModel(dir.File("abc.bin", "abc"));

        await vm.StartAsync();

        Assert.False(vm.HasError);
        Assert.True(vm.HasResults);
        Assert.False(vm.IsCalculating);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", vm.Md5);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", vm.Sha1);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", vm.Sha256);
        Assert.Equal("ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f", vm.Sha512);
        Assert.Equal(100, vm.ProgressPercent);
        Assert.Contains(vm.Sha512, vm.AllHashes);
    }

    private static string CreateLargeFile(TempDir dir, string name, long length)
    {
        var path = Path.Combine(dir.Path, name);
        using var stream = File.Create(path);
        stream.SetLength(length);
        return path;
    }

    [AvaloniaFact]
    public async Task Hash_CanBeCancelled()
    {
        using var dir = new TempDir();
        var vm = new HashViewModel(CreateLargeFile(dir, "big.bin", 64L * 1024 * 1024));

        var run = vm.StartAsync();
        Assert.True(vm.IsCalculating);
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await run;

        Assert.False(vm.IsCalculating);
        Assert.False(vm.HasResults);
        Assert.False(vm.HasError);
        Assert.Equal("Cancelled", vm.StatusText);
        Assert.False(vm.CancelCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task HashWindow_ClosingCancelsTheCalculation()
    {
        using var dir = new TempDir();
        var vm = new HashViewModel(CreateLargeFile(dir, "big.bin", 64L * 1024 * 1024));
        var window = new HashWindow { DataContext = vm };
        window.Show();

        var run = vm.StartAsync();
        Assert.True(vm.IsCalculating);
        window.Close();
        await run;

        Assert.False(vm.IsCalculating);
        Assert.Equal("Cancelled", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task SearchWindow_ClosingCancelsTheSearch()
    {
        using var dir = new TempDir();
        var gate = new TaskCompletionSource();
        var fileSystem = new DelegatingFileSystem(FileSystem)
        {
            BeforeList = _ => gate.Task
        };
        dir.File("a.txt");
        var vm = new SearchViewModel(fileSystem, dir.Path);
        var window = new SearchWindow { DataContext = vm };
        window.Show();

        var run = vm.StartSearchCommand.ExecuteAsync(null);
        Assert.True(vm.IsSearching);
        window.Close();
        gate.SetResult();
        await run;

        Assert.False(vm.IsSearching);
        Assert.StartsWith("Search cancelled", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Hash_MissingFile_IsReportedAsError()
    {
        var vm = new HashViewModel(Path.Combine(Path.GetTempPath(), "sharpcommander-missing-" + Guid.NewGuid().ToString("N")));

        await vm.StartAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.HasResults);
        Assert.Equal("File not found.", vm.ErrorMessage);
    }

    // ---- Properties (L5) -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Properties_ComputesFolderSizeAndCounts()
    {
        using var dir = new TempDir();
        dir.File("one.txt", new string('a', 10));
        dir.File("two.txt", new string('b', 20));
        dir.File("sub/three.txt", new string('c', 5));

        var vm = new PropertiesViewModel(DirectoryEntry(dir.Path), FileSystem);
        Assert.True(vm.IsDirectory);
        Assert.Equal("Folder", vm.TypeText);

        await vm.StartAsync();

        Assert.False(vm.IsCalculating);
        Assert.Contains("(35 bytes)", vm.SizeText);
        Assert.Equal("3 files, 1 folder", vm.ContentsText);
    }

    [AvaloniaFact]
    public async Task Properties_FolderMeasurementCanBeCancelled()
    {
        using var dir = new TempDir();
        dir.File("one.txt", "1");
        var gate = new TaskCompletionSource();
        var fileSystem = new DelegatingFileSystem(FileSystem)
        {
            BeforeDirectorySize = (_, token) => gate.Task.WaitAsync(token)
        };
        var vm = new PropertiesViewModel(DirectoryEntry(dir.Path), fileSystem);

        var run = vm.StartAsync();
        Assert.True(vm.IsCalculating);
        Assert.Equal("Calculating...", vm.SizeText);
        vm.Cancel();
        await run;

        Assert.False(vm.IsCalculating);
        Assert.Equal("Cancelled", vm.SizeText);
        Assert.Equal("Cancelled", vm.ContentsText);
        gate.TrySetResult();
    }

    [Fact]
    public void Properties_DescribesFiles()
    {
        using var dir = new TempDir();
        var vm = new PropertiesViewModel(FileEntry(dir.File("doc.txt", "abc")), FileSystem);

        Assert.True(vm.IsFile);
        Assert.Equal("File (.txt)", vm.TypeText);
        Assert.Contains("(3 bytes)", vm.SizeText);
        Assert.Equal(dir.Path, vm.Location);
        Assert.Equal("-", vm.AttributesText);
    }

    // ---- Viewer (D5) -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Viewer_DecodesTextAndFindsMatches()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "bom.txt");
        File.WriteAllText(path, "line one\nline two", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var vm = new ViewerViewModel(path);
        await vm.LoadAsync();

        Assert.False(vm.IsBinary);
        Assert.Equal("UTF-8 with BOM", vm.EncodingName);
        Assert.Equal("line one\nline two", vm.Text);
        Assert.Equal(2, vm.LineCount);
        Assert.Contains("UTF-8 with BOM", vm.StatusText);

        var matches = new List<int>();
        vm.MatchFound += (_, index) => matches.Add(index);
        vm.FindText = "LINE";
        Assert.True(vm.FindNextCommand.CanExecute(null));
        vm.FindNextCommand.Execute(null);
        vm.FindNextCommand.Execute(null);
        vm.FindNextCommand.Execute(null); // wraps around
        Assert.Equal(new[] { 0, 9, 0 }, matches);
        Assert.Equal(4, vm.MatchLength);
        Assert.Contains("Wrapped", vm.FindStatus);
    }

    [AvaloniaFact]
    public async Task Viewer_FallsBackToLatin1ForInvalidUtf8()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "latin1.txt");
        File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xE9]); // "cafe" with an accented e in ISO 8859-1

        var vm = new ViewerViewModel(path);
        await vm.LoadAsync();

        Assert.False(vm.IsBinary);
        Assert.StartsWith("Latin-1", vm.EncodingName);
        Assert.Equal("café", vm.Text);
    }

    [AvaloniaFact]
    public async Task Viewer_ShowsBinaryFilesAsHexDump()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "blob.bin");
        var bytes = new byte[40];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        File.WriteAllBytes(path, bytes);

        var vm = new ViewerViewModel(path);
        await vm.LoadAsync();

        Assert.True(vm.IsBinary);
        Assert.Equal(string.Empty, vm.Text);
        Assert.False(vm.FindNextCommand.CanExecute(null));
        Assert.Equal(3, vm.HexLines.Count);
        Assert.StartsWith("00000000  00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F  |", vm.HexLines[0]);
        Assert.StartsWith("00000020  20 21 22 23 24 25 26 27", vm.HexLines[2]);
        Assert.EndsWith("| !\"#$%&'|", vm.HexLines[2]);
        Assert.Contains("Binary", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task ViewerWindow_ShowsHexListForBinaryFiles()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "blob.bin");
        File.WriteAllBytes(path, new byte[64]);

        var vm = new ViewerViewModel(path);
        var window = new ViewerWindow { DataContext = vm };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        var hex = window.FindControl<ListBox>("HexView")!;
        var text = window.FindControl<TextBox>("TextView")!;
        Assert.True(hex.IsVisible);
        Assert.False(text.IsVisible);
        Assert.Equal(4, hex.ItemCount);
        window.Close();
    }

    // ---- Secondary windows load their XAML ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SecondaryWindows_LoadTheirXaml()
    {
        using var dir = new TempDir();
        var file = dir.File("doc.txt", "abc");

        var hashVm = new HashViewModel(file);
        var hash = new HashWindow { DataContext = hashVm };
        hash.Show();
        await hashVm.StartAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(hashVm.HasResults);
        hash.Close();

        var search = new SearchWindow { DataContext = new SearchViewModel(FileSystem, dir.Path) };
        search.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(search.FindControl<ListBox>("ResultsList"));
        search.Close();

        var propertiesVm = new PropertiesViewModel(DirectoryEntry(dir.Path), FileSystem);
        var properties = new PropertiesWindow { DataContext = propertiesVm };
        properties.Show();
        await propertiesVm.StartAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 file, 0 folders", propertiesVm.ContentsText);
        properties.Close();
    }

    // ---- Input dialog with validation --------------------------------------------------------------------

    [AvaloniaFact]
    public void InputDialog_ValidationDisablesOk()
    {
        var dialog = new InputDialog("Rename", "Name:", "file.txt", value => value.Contains('/') ? "No separators" : null);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var box = dialog.FindControl<TextBox>("InputBox")!;
        var ok = dialog.FindControl<Button>("OkButton")!;
        var error = dialog.FindControl<TextBlock>("ErrorText")!;
        Assert.True(ok.IsEnabled);
        Assert.False(error.IsVisible);

        box.Text = "bad/name";
        Assert.False(ok.IsEnabled);
        Assert.True(error.IsVisible);
        Assert.Equal("No separators", error.Text);

        box.Text = "good.txt";
        Assert.True(ok.IsEnabled);
        Assert.False(error.IsVisible);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_InputDialog_ReturnsTextOnEnterAndNullOnEscape()
    {
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowInputDialogAsync("New Folder", "Name:", "Folder");
        var dialog = OwnedDialog<InputDialog>(owner);
        dialog.FindControl<TextBox>("InputBox")!.Text = "Documents";
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("Documents", await pending);

        pending = service.ShowInputDialogAsync("New Folder", "Name:", "Folder");
        dialog = OwnedDialog<InputDialog>(owner);
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Null(await pending);
        owner.Close();
    }

    // ---- Confirm, delete, conflict, error dialogs through the service ----------------------------------------

    [AvaloniaFact]
    public async Task DialogService_Confirm_EnterConfirmsAndEscapeCancels()
    {
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowConfirmAsync("Run program", "Run it?", "Run", "Do not run", destructive: true);
        var dialog = OwnedDialog<ConfirmDialog>(owner);
        Assert.Equal("Run program", dialog.Title);
        Assert.Contains("destructive", dialog.FindControl<Button>("ConfirmButton")!.Classes);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(await pending);

        pending = service.ShowConfirmAsync("Run program", "Run it?");
        dialog = OwnedDialog<ConfirmDialog>(owner);
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_DeleteConfirm_DefaultsToTrashUnlessPermanentRequested()
    {
        using var dir = new TempDir();
        var items = Enumerable.Range(0, 7).Select(i => FileEntry(dir.File($"f{i}.txt"))).ToList();
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowDeleteConfirmAsync(items, trashAvailable: true, permanentRequested: false);
        var dialog = OwnedDialog<DeleteConfirmDialog>(owner);
        Assert.Contains("7 items", dialog.FindControl<TextBlock>("HeadingText")!.Text);
        Assert.Equal(5, dialog.FindControl<StackPanel>("NamesPanel")!.Children.Count);
        Assert.Equal("and 2 more", dialog.FindControl<TextBlock>("MoreText")!.Text);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(DeleteChoice.Trash, await pending);

        pending = service.ShowDeleteConfirmAsync(items, trashAvailable: true, permanentRequested: true);
        dialog = OwnedDialog<DeleteConfirmDialog>(owner);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(DeleteChoice.Permanent, await pending);

        pending = service.ShowDeleteConfirmAsync(items, trashAvailable: false, permanentRequested: false);
        dialog = OwnedDialog<DeleteConfirmDialog>(owner);
        Assert.False(dialog.FindControl<Button>("TrashButton")!.IsVisible);
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(DeleteChoice.Cancel, await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_Conflict_ReturnsTheChosenResolution()
    {
        using var dir = new TempDir();
        var source = dir.File("src/report.txt", "new");
        var destination = dir.File("dst/report.txt", "old");
        var conflict = new FileConflict(source, destination, false, 3, 3, DateTime.Now, DateTime.Now.AddDays(-1));
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowConflictAsync(conflict, remainingConflicts: 2);
        var dialog = OwnedDialog<ConflictDialog>(owner);
        var vm = (ConflictDialogViewModel)dialog.DataContext!;
        Assert.Equal("report (2).txt", vm.NewName);
        Assert.True(vm.CanOverwrite);
        Assert.True(vm.CanApplyToAll);
        Assert.True(vm.SourceIsNewer);

        vm.NewName = "report.txt";
        Assert.True(vm.HasRenameError);
        Assert.False(vm.RenameCommand.CanExecute(null));
        vm.NewName = "report-copy.txt";
        Assert.False(vm.HasRenameError);
        vm.ApplyToAll = true;
        vm.RenameCommand.Execute(null);

        var result = await pending;
        Assert.Equal(ConflictAction.Rename, result.Action);
        Assert.True(result.ApplyToAll);
        Assert.Equal("report-copy.txt", result.NewName);

        pending = service.ShowConflictAsync(conflict, remainingConflicts: 0);
        dialog = OwnedDialog<ConflictDialog>(owner);
        Assert.False(((ConflictDialogViewModel)dialog.DataContext!).CanApplyToAll);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // Skip is the default
        Assert.Equal(ConflictAction.Skip, (await pending).Action);

        pending = service.ShowConflictAsync(conflict, remainingConflicts: 0);
        dialog = OwnedDialog<ConflictDialog>(owner);
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(ConflictAction.Cancel, (await pending).Action);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_Conflict_RefusesOverwriteWhenKindsDiffer()
    {
        using var dir = new TempDir();
        var source = dir.File("src/data", "x");
        var destination = dir.Dir("dst/data"); // a folder is in the way of a file
        var conflict = new FileConflict(source, destination, false, 1, 0, DateTime.Now, DateTime.Now);
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowConflictAsync(conflict, 0);
        var dialog = OwnedDialog<ConflictDialog>(owner);
        var vm = (ConflictDialogViewModel)dialog.DataContext!;
        Assert.False(vm.CanOverwrite);
        Assert.False(vm.OverwriteCommand.CanExecute(null));
        Assert.Equal("Folder", vm.DestinationKind);
        Assert.Equal("File", vm.SourceKind);
        vm.SkipCommand.Execute(null);
        Assert.Equal(ConflictAction.Skip, (await pending).Action);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_ErrorDialogs_ShowAndClose()
    {
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowErrorAsync("Copy failed", "Access denied", "stack trace");
        var error = OwnedDialog<ErrorDialog>(owner);
        Assert.True(error.FindControl<Expander>("DetailsExpander")!.IsVisible);
        Assert.Equal("stack trace", error.FindControl<TextBox>("DetailsBox")!.Text);
        error.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await pending;

        var errors = new List<FileOperationError> { new("/a", "gone"), new("/b", "locked") };
        pending = service.ShowOperationErrorsAsync("Move", errors);
        var list = OwnedDialog<OperationErrorsDialog>(owner);
        Assert.Contains("2 items", list.FindControl<TextBlock>("SummaryText")!.Text);
        Assert.Equal(2, list.FindControl<ListBox>("ErrorsList")!.ItemCount);
        list.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await pending;
        Assert.Empty(owner.OwnedWindows);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_WithoutOwner_ReturnsSafeDefaults()
    {
        var service = new DialogService(FileSystem, () => null);
        var conflict = new FileConflict("a", "b", false, 0, 0, default, default);

        Assert.False(await service.ShowConfirmAsync("t", "m"));
        Assert.Equal(DeleteChoice.Cancel, await service.ShowDeleteConfirmAsync([], true, false));
        Assert.Equal(ConflictAction.Skip, (await service.ShowConflictAsync(conflict, 0)).Action);
        Assert.Null(await service.ShowInputDialogAsync("t", "p"));
        Assert.Null(await service.ShowAdvancedSearchDialogAsync(Path.GetTempPath()));
        Assert.False(await service.ShowMassRenameDialogAsync([]));
        await service.ShowErrorAsync("t", "m");
        await service.ShowOperationErrorsAsync("t", []);
    }

    // ---- audit regressions: armed buttons and keyboard dismissal -----------------------------------------

    [AvaloniaFact]
    public async Task DialogService_Confirm_WithDefaultIsCancel_ArmsTheSafeButton()
    {
        // The permanent-delete fallback follows the delete confirmation the user just answered with Enter, and
        // on Linux without gio it appears within milliseconds. A repeating Enter must not delete.
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowConfirmAsync("Delete permanently?", "The trash refused it.", "Delete permanently",
            "Skip", destructive: true, defaultIsCancel: true);
        var dialog = OwnedDialog<ConfirmDialog>(owner);

        var confirm = dialog.FindControl<Button>("ConfirmButton")!;
        var cancel = dialog.FindControl<Button>("CancelButton")!;
        Assert.False(confirm.IsDefault);
        Assert.True(cancel.IsDefault);
        Assert.Contains("destructive", confirm.Classes);

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(await pending);

        owner.Close();
    }

    [AvaloniaFact]
    public async Task DialogService_Confirm_StandaloneDestructivePromptStillConfirmsOnEnter()
    {
        // Arming the safe button is opt-in: a prompt that stands on its own keeps Enter as "yes".
        var owner = new Window();
        owner.Show();
        var service = new DialogService(FileSystem, () => owner);

        var pending = service.ShowConfirmAsync("Run program", "Run it?", "Run", "Do not run", destructive: true);
        var dialog = OwnedDialog<ConfirmDialog>(owner);

        Assert.True(dialog.FindControl<Button>("ConfirmButton")!.IsDefault);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(await pending);

        owner.Close();
    }

    [AvaloniaFact]
    public void AboutWindow_IsDismissedWithEscapeAndEnter()
    {
        // Every other dialog in the project could be dismissed from the keyboard; About could not, and opened
        // with nothing focused at all.
        var owner = new Window();
        owner.Show();

        foreach (var key in new[] { PhysicalKey.Escape, PhysicalKey.Enter })
        {
            var about = new AboutWindow { DataContext = new AboutViewModel() };
            about.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.True(about.IsVisible);

            about.KeyPressQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(about.IsVisible);
        }

        owner.Close();
    }
}
