using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Localization;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;
using SharpCommander.Tests.Fakes;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace SharpCommander.Tests;

/// <summary>
/// Renders the pictures of the website from the real main window, in Spanish and in English: docs/img/hero,
/// light and sftp (each as a 1x and a 2x WebP; the English ones go to docs/img/en/) and docs/img/og-es.jpg and
/// og-en.jpg, the image social networks show for a link. The window is drawn at twice its size, so text and icons
/// are sharp on high-density screens rather than enlarged afterwards. It only runs when SC_SITE is "1":
/// <c>SC_SITE=1 dotnet test SharpCommander.sln --filter GenerateSiteImages</c>, then <c>dotnet run tools/SiteGen.cs</c>.
/// </summary>
public class SiteImageGenerator(ITestOutputHelper output)
{
    private const int Width = 1200;
    private const int Height = 760;

    [AvaloniaFact]
    public async Task GenerateSiteImages()
    {
        if (Environment.GetEnvironmentVariable("SC_SITE") != "1")
        {
            output.WriteLine("Skipped: set SC_SITE=1 to render the website images in docs/img.");
            return;
        }

        var language = Strings.CurrentLanguage;
        var docs = Path.Combine(RepoRoot(), "docs");

        try
        {
            foreach (var lang in new[] { "es", "en" })
            {
                var folder = lang == "es" ? Path.Combine(docs, "img") : Path.Combine(docs, "img", "en");
                Directory.CreateDirectory(folder);

                using var dir = new TempDir();
                var sample = Sample.Create(dir, lang);

                using var hero = await RenderWindowAsync(lang, "Dark", sample.Projects, sample.Photos, sample);
                SaveWebp(hero, folder, "hero");

                // The right panel on the Computer view shows the drives with their own icon.
                using var light = await RenderWindowAsync(lang, "Light", sample.Projects, string.Empty, sample);
                SaveWebp(light, folder, "light");

                using var sftp = await RenderSftpAsync(lang, sample);
                SaveWebp(sftp, folder, "sftp");

                using var og = await RenderSocialCardAsync(lang, hero);
                Save(og, SKEncodedImageFormat.Jpeg, 90, Path.Combine(docs, "img", $"og-{lang}.jpg"));
            }
        }
        finally
        {
            Strings.Use(language);
        }
    }

    // ---- the main window --------------------------------------------------------------------------------------

    private async Task<SKBitmap> RenderWindowAsync(string lang, string theme, string left, string right, Sample sample,
        IFileSystemService? fileSystem = null, SftpConnections? connections = null, Action<MainWindowViewModel>? arrange = null)
    {
        var settings = new FakeSettingsService();
        settings.Settings.Theme = theme;
        settings.Settings.Language = lang;
        settings.Settings.LastLeftPanelPath = left;
        settings.Settings.LastRightPanelPath = right;
        settings.Settings.CheckForUpdates = false;
        settings.Settings.Favorites.AddRange(sample.Favorites);

        var files = fileSystem ?? new FileSystemService();
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        using var viewModel = new MainWindowViewModel(files, settings, dialogs, new FakeClipboardService(), new FileOperationsService(files, dialogs, trash), trash,
            new ThemeService(), new CompositeArchiveService(new ZipArchiveService(), new TarArchiveService()), new DirectoryComparer(), new UndoService(),
            connections ?? new SftpConnections(), new FakeUpdateService(), new FakeSpaceAnalyzerService());
        var window = new MainWindow { DataContext = viewModel, Width = Width, Height = Height };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await window.Initialization;

            viewModel.SetActivePanel(viewModel.LeftPanel);
            viewModel.LeftPanel.SelectPath(sample.Highlighted);
            viewModel.LeftPanel.RequestFocus();
            arrange?.Invoke(viewModel);
            await PumpAsync();

            return await CaptureAtTwiceTheSizeAsync(window);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The left panel on a local folder and the right one on a web server's files, over SFTP.</summary>
    private async Task<SKBitmap> RenderSftpAsync(string lang, Sample sample)
    {
        var server = new FakeSftpServer();
        server.AddDirectory("/home/ana/www");
        foreach (var name in new[] { "css", "img", "js", "fonts" })
        {
            server.AddDirectory("/home/ana/www/" + name);
        }

        server.AddFile("/home/ana/www/index.html", new string('x', 18_422));
        server.AddFile("/home/ana/www/" + (lang == "es" ? "contacto.html" : "contact.html"), new string('x', 7_310));
        server.AddFile("/home/ana/www/favicon.ico", new string('x', 4_286));
        server.AddFile("/home/ana/www/robots.txt", new string('x', 96));
        server.AddFile("/home/ana/www/sitemap.xml", new string('x', 1_204));

        var connections = new SftpConnections(() => server);
        await connections.ConnectAsync(new SftpSite { Host = "example.com", Port = 22, Username = "ana" }, null);
        var files = new RoutingFileSystemService(new FileSystemService(), connections);

        const string Remote = "sftp://ana@example.com:22/home/ana/www";
        return await RenderWindowAsync(lang, "Dark", sample.Website, Remote, sample, files, connections, viewModel =>
        {
            // The server's panel is the active one, as right after connecting, with the message the app shows then.
            viewModel.SetActivePanel(viewModel.RightPanel);
            viewModel.RightPanel.SelectPath(Remote + "/index.html");
            viewModel.RightPanel.RequestFocus();
            viewModel.StatusMessage = $"{DateTime.Now:HH:mm:ss}  Connected to ana@example.com.";
        });
    }

    /// <summary>
    /// Draws the window's content at twice the size inside a window twice as large: text and icons are rendered at
    /// that density, not enlarged afterwards.
    /// </summary>
    private static async Task<SKBitmap> CaptureAtTwiceTheSizeAsync(Window window)
    {
        var content = (Control)window.Content!;
        window.Content = null;
        window.Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(2, 2), Child = content };
        window.Width = Width * 2;
        window.Height = Height * 2;
        await PumpAsync();

        return Decode(window.CaptureRenderedFrame()!);
    }

    // ---- the picture for social networks ------------------------------------------------------------------

    /// <summary>1200 by 630: the brand, the promise and a large piece of the window running off the right edge.</summary>
    private static async Task<SKBitmap> RenderSocialCardAsync(string lang, SKBitmap hero)
    {
        var english = lang == "en";
        using var icon = new Bitmap(Path.Combine(RepoRoot(), "src", "SharpCommander.Desktop", "Resources", "icon.png"));
        using var shot = ToAvalonia(hero);

        var headline = new StackPanel { Spacing = 0 };
        foreach (var line in english
                     ? new[] { "Two panels.", "The whole keyboard.", "Three systems." }
                     : new[] { "Dos paneles.", "Todo el teclado.", "Tres sistemas." })
        {
            headline.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 58,
                FontWeight = FontWeight.ExtraBold,
                LetterSpacing = -1.6,
                LineHeight = 66,
                Foreground = headline.Children.Count == 0
                    ? Brushes.White
                    : new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#5aa2ff"), 0), new GradientStop(Color.Parse("#3fd0e0"), 0.6), new GradientStop(Color.Parse("#ffc83a"), 1) }
                    }
            });
        }

        var left = new StackPanel
        {
            Margin = new Thickness(68, 64, 0, 0),
            Spacing = 34,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children =
                    {
                        new Image { Source = icon, Width = 60, Height = 60 },
                        new TextBlock { Text = "SharpCommander", FontSize = 32, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                headline,
                new StackPanel
                {
                    Spacing = 12,
                    Margin = new Thickness(0, 26, 0, 0),
                    Children =
                    {
                        Pill("Windows · macOS · Linux"),
                        Pill(english ? "Free and open source" : "Gratis y de código abierto")
                    }
                }
            }
        };

        var window = new Border
        {
            Width = 760,
            Height = 481,
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 30 70 0 #99000000"),
            Child = new Image { Source = shot, Stretch = Stretch.UniformToFill }
        };
        Canvas.SetLeft(window, 560);
        Canvas.SetTop(window, 96);

        var card = new Canvas
        {
            Width = 1200,
            Height = 630,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0d1a33"), 0), new GradientStop(Color.Parse("#0b0f19"), 0.55), new GradientStop(Color.Parse("#0b0d12"), 1) }
            },
            Children = { left, window }
        };

        var host = new Window { Content = card, Width = 1200, Height = 630, SystemDecorations = SystemDecorations.None };
        try
        {
            host.Show();
            await PumpAsync();
            return Decode(host.CaptureRenderedFrame()!);
        }
        finally
        {
            host.Close();
        }

        static Border Pill(string text) => new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18, 9),
            Child = new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#e6ebf3")) }
        };
    }

    // ---- files ---------------------------------------------------------------------------------------------

    /// <summary>Writes name-2x.webp at the rendered size and name.webp at half of it.</summary>
    private void SaveWebp(SKBitmap twice, string folder, string name)
    {
        Save(twice, SKEncodedImageFormat.Webp, 84, Path.Combine(folder, name + "-2x.webp"));
        using var once = twice.Resize(new SKImageInfo(twice.Width / 2, twice.Height / 2), SKFilterQuality.High);
        Save(once, SKEncodedImageFormat.Webp, 86, Path.Combine(folder, name + ".webp"));
    }

    private void Save(SKBitmap bitmap, SKEncodedImageFormat format, int quality, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        File.WriteAllBytes(path, data.ToArray());
        output.WriteLine($"{Path.GetRelativePath(RepoRoot(), path)}: {bitmap.Width}x{bitmap.Height}, {data.Size / 1024} KB");
    }

    private static SKBitmap Decode(Bitmap frame)
    {
        using var png = new MemoryStream();
        frame.Save(png);
        png.Position = 0;
        return SKBitmap.Decode(png);
    }

    private static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(new MemoryStream(data.ToArray()));
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 12; i++)
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

    /// <summary>Made-up folders in the language of the page, so no real file of anyone's shows up on the website.</summary>
    private sealed record Sample(string Projects, string Photos, string Website, string Highlighted, IReadOnlyList<FavoriteItem> Favorites)
    {
        public static Sample Create(TempDir dir, string lang)
        {
            var es = lang == "es";
            var stamp = new DateTime(2026, 9, 21, 9, 30, 0);

            // Dates are given to the folders last: adding anything to a folder moves its date to now.
            var dates = new List<(string Path, DateTime Date)>();
            string Folder(TempDir root, string path, DateTime modified)
            {
                var full = Path.IsPathRooted(path) ? path : Path.Combine(root.Path, path);
                Directory.CreateDirectory(full);
                dates.Add((full, modified));
                return full;
            }

            var projects = Folder(dir, es ? "Proyectos" : "Projects", stamp);
            Folder(dir, Path.Combine(projects, es ? "Archivo" : "Archive"), stamp.AddMonths(-3));
            Folder(dir, Path.Combine(projects, es ? "Diseño" : "Design assets"), stamp.AddDays(-20));
            Folder(dir, Path.Combine(projects, "SharpCommander"), stamp.AddDays(-2));
            Folder(dir, Path.Combine(projects, es ? "Web" : "Website"), stamp.AddDays(-9));
            var readme = File(Path.Combine(projects, es ? "LEEME.md" : "README.md"), 2_480, stamp.AddDays(-1));
            File(Path.Combine(projects, es ? "presupuesto.xlsx" : "budget.xlsx"), 34_812, stamp.AddDays(-6));
            File(Path.Combine(projects, es ? "presentacion.pptx" : "kickoff-deck.pptx"), 1_842_233, stamp.AddDays(-15));
            File(Path.Combine(projects, es ? "notas.txt" : "notes.txt"), 512, stamp.AddHours(-3));
            File(Path.Combine(projects, es ? "novedades.pdf" : "release-notes.pdf"), 218_004, stamp.AddDays(-8));
            File(Path.Combine(projects, es ? "hoja-de-ruta-2026.md" : "roadmap-2026.md"), 1_115, stamp.AddDays(-4));
            File(Path.Combine(projects, es ? "pendientes.txt" : "todo.txt"), 96, stamp);

            var photos = Folder(dir, es ? "Fotos" : "Photos", stamp);
            Folder(dir, Path.Combine(photos, es ? "Verano 2026" : "Summer 2026"), stamp.AddDays(-30));
            Folder(dir, Path.Combine(photos, es ? "Familia" : "Family"), stamp.AddMonths(-2));
            Folder(dir, Path.Combine(photos, es ? "Capturas" : "Screenshots"), stamp.AddDays(-12));
            File(Path.Combine(photos, "IMG_0412.jpg"), 2_418_611, stamp.AddDays(-30));
            File(Path.Combine(photos, "IMG_0413.jpg"), 2_602_100, stamp.AddDays(-30).AddMinutes(2));
            File(Path.Combine(photos, "IMG_0417.jpg"), 1_998_431, stamp.AddDays(-30).AddMinutes(9));
            File(Path.Combine(photos, es ? "faro.png" : "lighthouse.png"), 4_120_774, stamp.AddDays(-33));
            File(Path.Combine(photos, es ? "panoramica.tif" : "panorama.tif"), 9_812_006, stamp.AddDays(-35));
            File(Path.Combine(photos, es ? "atardecer.mp4" : "sunset.mp4"), 12_480_000, stamp.AddDays(-31));

            var website = Folder(dir, Path.Combine(projects, es ? "Web" : "Website"), stamp.AddDays(-9));
            foreach (var name in new[] { "css", "img", "js", "fonts" })
            {
                Folder(dir, Path.Combine(website, name), stamp.AddDays(-9));
            }

            File(Path.Combine(website, "index.html"), 18_422, stamp.AddDays(-1));
            File(Path.Combine(website, es ? "contacto.html" : "contact.html"), 7_310, stamp.AddDays(-5));
            File(Path.Combine(website, "favicon.ico"), 4_286, stamp.AddMonths(-4));
            File(Path.Combine(website, "robots.txt"), 96, stamp.AddMonths(-4));
            File(Path.Combine(website, "sitemap.xml"), 1_204, stamp.AddDays(-1));

            // The folders every user has, named as they are on disk, with their own glyph in the favorites.
            var favorites = new List<FavoriteItem>();
            foreach (var key in new[] { "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "Home" })
            {
                favorites.Add(new FavoriteItem { Name = key, Path = Folder(dir, key, stamp), IconKey = key, IsSystem = true, Order = favorites.Count });
            }

            favorites.Add(new FavoriteItem { Name = Path.GetFileName(projects), Path = projects, Order = favorites.Count });
            favorites.Add(new FavoriteItem { Name = Path.GetFileName(photos), Path = photos, Order = favorites.Count });

            foreach (var (path, date) in Enumerable.Reverse(dates))
            {
                Directory.SetLastWriteTime(path, date);
            }

            return new Sample(projects, photos, website, readme, favorites);
        }

        private static string File(string path, long length, DateTime modified)
        {
            using (var stream = System.IO.File.Create(path))
            {
                stream.SetLength(length);
            }

            System.IO.File.SetLastWriteTime(path, modified);
            return path;
        }
    }
}
