using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SaveStateAsync();
        }
    }

    private void LeftPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetActivePanel(viewModel.LeftPanel);
        }
    }

    private void RightPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetActivePanel(viewModel.RightPanel);
        }
    }

    private void Favorites_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && 
            listBox.SelectedItem is FavoriteItem favorite &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ActivePanel?.NavigateToFavoriteCommand.Execute(favorite);
            listBox.SelectedItem = null; // Reset selection
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var aboutDialog = new AboutWindow
        {
            DataContext = new AboutViewModel()
        };
        aboutDialog.ShowDialog(this);
    }

    private void ThemeLight_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private void ThemeDark_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private void ThemeDefault_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
