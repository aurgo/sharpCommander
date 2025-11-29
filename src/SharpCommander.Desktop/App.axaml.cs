using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;

namespace SharpCommander.Desktop;

/// <summary>
/// Main application class for SharpCommander.
/// </summary>
public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", 
        Justification = "Avalonia's DataValidators is safe to use with compiled bindings")]
    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid duplicate validation plugins - safe with compiled bindings
        if (BindingPlugins.DataValidators.Count > 0)
        {
            BindingPlugins.DataValidators.RemoveAt(0);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var fileSystemService = new FileSystemService();
            var settingsService = new SettingsService();
            var mainViewModel = new MainWindowViewModel(fileSystemService, settingsService);
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
