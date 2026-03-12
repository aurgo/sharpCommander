using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;

namespace SharpCommander.Desktop.Services;

public class DialogService : IDialogService
{
    public async Task ShowHashDialogAsync(string filePath)
    {
        var viewModel = new HashViewModel(filePath);
        var window = new HashWindow
        {
            DataContext = viewModel
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    public async Task ShowAdvancedSearchDialogAsync(string initialPath)
    {
        var fileSystemService = new FileSystemService();
        var viewModel = new SearchViewModel(fileSystemService, initialPath);
        var window = new SearchWindow
        {
            DataContext = viewModel
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    public async Task ShowMassRenameDialogAsync(IEnumerable<string> filePaths)
    {
        var viewModel = new MassRenameViewModel(filePaths);
        var window = new MassRenameWindow
        {
            DataContext = viewModel
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    public async Task ShowPropertiesDialogAsync(FileSystemEntry entry)
    {
        var window = new PropertiesWindow
        {
            DataContext = entry
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await window.ShowDialog(desktop.MainWindow!);
        }
    }

    public async Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue = "")
    {
        var dialog = new InputDialog(title, prompt, initialValue);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var result = await dialog.ShowDialog<bool>(desktop.MainWindow!);
            return result ? dialog.Result : null;
        }
        return null;
    }
}
