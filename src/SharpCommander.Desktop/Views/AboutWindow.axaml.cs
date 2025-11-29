using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// About dialog window.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Website_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel viewModel)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = viewModel.Website,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch
            {
                // Ignore errors when opening website
            }
        }
    }
}
