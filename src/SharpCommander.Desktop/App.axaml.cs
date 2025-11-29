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

    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid duplicate validation plugins
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var fileSystemService = new FileSystemService();
            var mainViewModel = new MainWindowViewModel(fileSystemService);
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
