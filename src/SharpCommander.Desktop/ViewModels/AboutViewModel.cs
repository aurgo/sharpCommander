using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// ViewModel for the About dialog.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    public string ProductName => "SharpCommander";
    
    public string Version => GetVersion();
    
    public string Copyright => $"© {DateTime.Now.Year} AURGO. All rights reserved.";
    
    public string Description => "A modern, cross-platform dual-pane file manager built with Avalonia UI. " +
                                "SharpCommander provides a powerful and intuitive way to manage your files " +
                                "across Windows, Linux, and macOS.";
    
    public string Website => "https://github.com/aurgo/sharpCommander";
    
    public string License => "MIT License";

    public IReadOnlyList<string> Features =>
    [
        "🖥️ Cross-platform: Works on Windows, Linux, and macOS",
        "📁 Dual-pane interface for efficient file management",
        "⚡ Fast file operations with progress tracking",
        "🔍 Real-time file system monitoring",
        "🎨 Modern Fluent Design with dark/light theme support",
        "⌨️ Keyboard shortcuts for power users",
        "📊 Detailed file information and statistics"
    ];

    [RelayCommand]
    private Task OpenWebsiteAsync()
    {
        // This will be handled in the View layer
        return Task.CompletedTask;
    }

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "2.0.0";
    }
}
