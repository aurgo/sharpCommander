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

    public ConfirmDialog(string title, string message, string confirmText, string cancelText, bool destructive,
        bool defaultIsCancel = false)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;

        // A prompt that follows another confirmation arms the safe button instead of its own: the trash refusing
        // an item asks again right after the user answered "Move to Trash" with Enter, so a still-repeating or
        // reflexive second Enter would carry straight through to a permanent delete. Same convention as
        // ConflictDialog, where Skip is the default and Overwrite is not.
        var defaultButton = defaultIsCancel ? CancelButton : ConfirmButton;
        ConfirmButton.IsDefault = !defaultIsCancel;
        CancelButton.IsDefault = defaultIsCancel;

        if (destructive)
        {
            ConfirmButton.Classes.Add("destructive");
        }

        Opened += (_, _) => Dispatcher.UIThread.Post(() => defaultButton.Focus(), DispatcherPriority.Input);
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
