using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Yes/no question. Enter confirms, Escape cancels; a destructive confirmation gets a red confirm button.
/// The dialog result is true when confirmed.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message, string confirmText, string cancelText, bool destructive)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        if (destructive)
        {
            ConfirmButton.Classes.Add("destructive");
        }

        Opened += (_, _) => Dispatcher.UIThread.Post(() => ConfirmButton.Focus(), DispatcherPriority.Input);
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
