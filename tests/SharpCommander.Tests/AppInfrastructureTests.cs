using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using SharpCommander.Core.Models;
using SharpCommander.Desktop;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Process-wide plumbing: the UI-thread exception handler (H3), the rotating file log (L9), the single-sourced
/// version (L11) and the theme applied at startup (M11).
/// </summary>
public class AppInfrastructureTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpCommander.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("SharpCommander.sln not found above " + AppContext.BaseDirectory);
    }

    // ---- H3 ---------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DispatcherExceptions_AreLoggedShownOnceAndSwallowed()
    {
        var app = Assert.IsType<App>(Application.Current);
        var dialogs = new FakeDialogService();

        using (app.RegisterDispatcherExceptionHandler(dialogs))
        {
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("posted failure"));
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("failure while the dialog is open"));

            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
        }

        Assert.Equal(["error:Unexpected error"], dialogs.Calls);
        Assert.Equal(["posted failure"], dialogs.ErrorMessages);
    }

    // ---- L9 ---------------------------------------------------------------------------------------------

    [Fact]
    public void AppLog_WritesLevelsRotatesAtOneMebibyteAndNeverThrows()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Dir("logs"), "app.log");
        AppLog.UseLogFile(file);
        try
        {
            AppLog.Info("first line");
            Assert.Contains("[INFO ] first line", File.ReadAllText(file));

            // Rotation happens on the first write that finds the file over the limit, so filling it up to
            // just past 1 MiB never rotates; the next line does.
            var filler = new string('x', 64 * 1024);
            var writes = 0;
            while (new FileInfo(file).Length <= 1024 * 1024 && writes++ < 32)
            {
                AppLog.Warning(filler);
            }

            Assert.True(new FileInfo(file).Length > 1024 * 1024);
            Assert.False(File.Exists(file + ".1"));

            AppLog.Error("after rotation", new InvalidOperationException("boom"));

            Assert.True(File.Exists(file + ".1"));
            var current = File.ReadAllText(file);
            Assert.Contains("[ERROR] after rotation", current);
            Assert.Contains("InvalidOperationException: boom", current);
            Assert.True(new FileInfo(file).Length < 64 * 1024);

            AppLog.UseLogFile(Path.Combine(dir.Path, "missing", "app.log"));
            AppLog.Error("the directory does not exist; logging must stay silent");
        }
        finally
        {
            AppLog.UseLogFile(null);
        }
    }

    // ---- L11 --------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Version_IsSingleSourcedFromDirectoryBuildProps()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var expected = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Matches(@"^\d+\.\d+\.\d+", expected);
        var numeric = Regex.Match(expected, @"^\d+\.\d+\.\d+").Value;

        var informational = typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var plus = informational.IndexOf('+');
        Assert.Equal(expected, plus > 0 ? informational[..plus] : informational);
        Assert.Equal(numeric, typeof(MainWindowViewModel).Assembly.GetName().Version!.ToString(3));
        Assert.Equal(numeric, typeof(FileSystemEntry).Assembly.GetName().Version!.ToString(3));
        Assert.Equal(numeric, new AboutViewModel().Version);

        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        var fileSystem = new FileSystemService();
        using var vm = new MainWindowViewModel(fileSystem, new FakeSettingsService(), dialogs, new FakeClipboardService(), new FileOperationsService(fileSystem, dialogs, trash), trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), new FakeUpdateService(), new FakeSpaceAnalyzerService());
        Assert.Equal(expected, vm.Version);
    }

    // ---- M11 --------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Startup_AppliesTheSavedThemeAndFolders()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var settings = new FakeSettingsService();
        settings.Settings.Theme = "dark";
        settings.Settings.LastLeftPanelPath = left;
        settings.Settings.LastRightPanelPath = right;
        settings.Settings.FavoritesPanelVisible = false;
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        var fileSystem = new FileSystemService();
        using var vm = new MainWindowViewModel(fileSystem, settings, dialogs, new FakeClipboardService(), new FileOperationsService(fileSystem, dialogs, trash), trash, new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(), new SftpConnections(), new FakeUpdateService(), new FakeSpaceAnalyzerService());
        var application = Application.Current!;
        var original = application.RequestedThemeVariant;

        try
        {
            await vm.InitializeAsync();

            Assert.Equal("Dark", vm.CurrentTheme);
            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.False(vm.ShowFavoritesPanel);
            Assert.Equal(left, vm.LeftPanel.CurrentPath);
            Assert.Equal(right, vm.RightPanel.CurrentPath);
            Assert.Contains("Ready.", vm.StatusMessage);
            Assert.Empty(dialogs.Calls);
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }
}
