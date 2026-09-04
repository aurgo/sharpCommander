using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

        // Without an initial focus the window opens with nothing focused, so a keyboard user has to Tab before
        // the Close button can even be reached; IsDefault/IsCancel then make Enter and Escape dismiss it.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => CloseButton.Focus(), DispatcherPriority.Input);
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
