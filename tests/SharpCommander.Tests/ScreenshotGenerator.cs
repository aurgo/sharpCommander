using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;
using SharpCommander.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace SharpCommander.Tests;

/// <summary>
/// Renders the real main window with Skia, populated with sample folders, and writes docs/screenshot.png for
/// the README. It only runs when the environment variable SC_SCREENSHOT is "1":
/// <c>SC_SCREENSHOT=1 dotnet test SharpCommander.sln --filter GenerateReadmeScreenshot</c>.
/// </summary>
public class ScreenshotGenerator(ITestOutputHelper output)
{
    private const int Width = 1200;
    private const int Height = 800;

    [AvaloniaFact]
    public async Task GenerateReadmeScreenshot()
    {
        if (Environment.GetEnvironmentVariable("SC_SCREENSHOT") != "1")
        {
            output.WriteLine("Skipped: set SC_SCREENSHOT=1 to render docs/screenshot.png.");
            return;
        }

        using var dir = new TempDir();
        var projects = CreateProjectsFolder(dir);
        var photos = CreatePhotosFolder(dir);

        var settings = new FakeSettingsService();
        settings.Settings.Theme = "Dark";
        settings.Settings.LastLeftPanelPath = projects;
        settings.Settings.LastRightPanelPath = photos;
        await settings.AddFavoriteAsync(projects, "Projects");
        await settings.AddFavoriteAsync(photos, "Photos");

        var fileSystem = new FileSystemService();
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        var operations = new FileOperationsService(fileSystem, dialogs, trash);
        using var viewModel = new MainWindowViewModel(fileSystem, settings, dialogs, new FakeClipboardService(), operations, trash, new ThemeService());
        var window = new MainWindow { DataContext = viewModel, Width = Width, Height = Height };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await window.Initialization;

            viewModel.SetActivePanel(viewModel.LeftPanel);
            viewModel.LeftPanel.SelectPath(Path.Combine(projects, "README.md"));
            viewModel.LeftPanel.RequestFocus();
            await PumpAsync();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var target = Path.Combine(RepoRoot(), "docs", "screenshot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            frame.Save(target);

            var size = new FileInfo(target).Length;
            output.WriteLine($"Screenshot written to {target} ({size:N0} bytes, {frame.PixelSize.Width}x{frame.PixelSize.Height}).");
            Assert.True(size > 20 * 1024, $"The screenshot is suspiciously small ({size} bytes).");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpCommander.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("SharpCommander.sln not found above " + AppContext.BaseDirectory);
    }

    private static string CreateProjectsFolder(TempDir dir)
    {
        var root = dir.Dir("Projects");
        var stamp = new DateTime(2026, 8, 12, 9, 30, 0);

        Folder(dir, "Projects/SharpCommander", stamp.AddDays(-2));
        Folder(dir, "Projects/Website", stamp.AddDays(-9));
        Folder(dir, "Projects/Archive", stamp.AddMonths(-3));
        Folder(dir, "Projects/Design assets", stamp.AddDays(-20));
        Sample(dir, "Projects/README.md", 2_480, stamp.AddDays(-1));
        Sample(dir, "Projects/roadmap-2026.md", 1_115, stamp.AddDays(-4));
        Sample(dir, "Projects/budget.xlsx", 34_812, stamp.AddDays(-6));
        Sample(dir, "Projects/kickoff-deck.pptx", 1_842_233, stamp.AddDays(-15));
        Sample(dir, "Projects/notes.txt", 512, stamp.AddHours(-3));
        Sample(dir, "Projects/release-notes.pdf", 218_004, stamp.AddDays(-8));
        Sample(dir, "Projects/todo.txt", 96, stamp);
        Directory.SetLastWriteTime(root, stamp);
        return root;
    }

    private static string CreatePhotosFolder(TempDir dir)
    {
        var root = dir.Dir("Photos");
        var stamp = new DateTime(2026, 7, 26, 18, 5, 0);

        Folder(dir, "Photos/2026 Summer trip", stamp.AddDays(-1));
        Folder(dir, "Photos/Family", stamp.AddMonths(-2));
        Folder(dir, "Photos/Screenshots", stamp.AddDays(-12));
        Sample(dir, "Photos/IMG_0412.jpg", 2_418_611, stamp);
        Sample(dir, "Photos/IMG_0413.jpg", 2_602_100, stamp.AddMinutes(2));
        Sample(dir, "Photos/IMG_0417.jpg", 1_998_431, stamp.AddMinutes(9));
        Sample(dir, "Photos/lighthouse.png", 4_120_774, stamp.AddDays(-3));
        Sample(dir, "Photos/panorama.tif", 9_812_006, stamp.AddDays(-5));
        Sample(dir, "Photos/sunset.mp4", 12_480_000, stamp.AddDays(-1));
        Directory.SetLastWriteTime(root, stamp);
        return root;
    }

    private static void Folder(TempDir dir, string relative, DateTime modified)
    {
        var path = dir.Dir(relative);
        Directory.SetLastWriteTime(path, modified);
    }

    private static void Sample(TempDir dir, string relative, long length, DateTime modified)
    {
        var path = Path.Combine(dir.Path, relative);
        using (var stream = File.Create(path))
        {
            stream.SetLength(length);
        }

        File.SetLastWriteTime(path, modified);
    }
}
