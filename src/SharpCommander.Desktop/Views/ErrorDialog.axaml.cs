using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Shows an error message with an optional expandable details section (selectable, copyable).
/// </summary>
public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string title, string message, string? details)
        : this()
    {
        Title = title;
        MessageText.Text = message;

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsBox.Text = details;
            DetailsExpander.IsVisible = true;
        }

        Opened += (_, _) => Dispatcher.UIThread.Post(() => OkButton.Focus(), DispatcherPriority.Input);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(DetailsBox.Text ?? string.Empty);
            }
        }
        catch (Exception)
        {
            // The clipboard is a convenience; a failure to copy must not surface as another error.
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
