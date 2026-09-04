using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Shows the checksums computed by a <see cref="HashViewModel"/>. Closing the window cancels a running
/// calculation; the copy buttons carry the text to copy in their Tag.
/// </summary>
public partial class HashWindow : Window
{
    public HashWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as HashViewModel)?.Cancel();
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: string text } && text.Length > 0 && Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception)
        {
            // Copying is a convenience; a clipboard failure must not surface as an error.
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
