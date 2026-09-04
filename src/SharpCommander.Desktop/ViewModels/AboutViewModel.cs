using System.Reflection;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Data for the About dialog. The website link is opened by the view.
/// </summary>
public sealed class AboutViewModel
{
    public string ProductName => "SharpCommander";

    /// <summary>Gets the application version as built (single-sourced from Directory.Build.props).</summary>
    public string Version { get; } = GetVersion();

    public string Copyright => $"Copyright {DateTime.Now.Year} AURGO. All rights reserved.";

    public string Description => "A modern, cross-platform dual-pane file manager built with Avalonia UI. " +
                                "SharpCommander provides a powerful and intuitive way to manage your files " +
                                "across Windows, Linux, and macOS.";

    public string Website => "https://github.com/aurgo/sharpCommander";

    public string License => "MIT License";

    public IReadOnlyList<string> Features { get; } =
    [
        "Cross-platform: works on Windows, Linux, and macOS",
        "Dual-pane interface for efficient file management",
        "File operations with real progress, conflict handling and cancellation",
        "Automatic refresh when the folder changes on disk",
        "Fluent design with dark and light themes",
        "Keyboard shortcuts for power users",
        "Internal viewer, checksums, search and mass rename tools"
    ];

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
    }
}
