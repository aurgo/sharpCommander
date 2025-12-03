using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SharpCommander.Core.Interfaces;
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
}
