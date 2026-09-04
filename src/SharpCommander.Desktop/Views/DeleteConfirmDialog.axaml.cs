using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Confirms deleting one or more entries, listing up to five names. "Move to Trash" is the default when a
/// trash is available and permanent deletion was not requested (Shift+Delete). The dialog result is a
/// <see cref="DeleteChoice"/>; closing the window means Cancel.
/// </summary>
public partial class DeleteConfirmDialog : Window
{
    private const int MaxListedNames = 5;

    public DeleteConfirmDialog()
    {
        InitializeComponent();
    }

    public DeleteConfirmDialog(IReadOnlyList<FileSystemEntry> items, bool trashAvailable, bool permanentRequested)
        : this()
    {
        ArgumentNullException.ThrowIfNull(items);

        var permanent = permanentRequested || !trashAvailable;
        Title = permanent ? "Delete permanently" : "Move to Trash";
        HeadingText.Text = items.Count == 1
            ? $"{(permanent ? "Delete" : "Move to Trash")} '{items[0].Name}'?"
            : $"{(permanent ? "Delete" : "Move to Trash")} {items.Count:N0} items?";

        if (items.Count > 1)
        {
            foreach (var item in items.Take(MaxListedNames))
            {
                NamesPanel.Children.Add(new TextBlock
                {
                    Text = item.EntryType == FileSystemEntryType.Directory ? $"{item.Name} (folder)" : item.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (items.Count > MaxListedNames)
            {
                MoreText.Text = $"and {items.Count - MaxListedNames:N0} more";
                MoreText.IsVisible = true;
            }
        }

        HintText.Text = permanent
            ? "This cannot be undone."
            : "Items in the Trash can be restored later.";

        TrashButton.IsVisible = trashAvailable;
        TrashButton.IsDefault = trashAvailable && !permanentRequested;
        PermanentButton.IsDefault = permanent;

        var primary = permanent ? PermanentButton : TrashButton;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => primary.Focus(), DispatcherPriority.Input);
    }

    private void Trash_Click(object? sender, RoutedEventArgs e)
    {
        Close(DeleteChoice.Trash);
    }

    private void Permanent_Click(object? sender, RoutedEventArgs e)
    {
        Close(DeleteChoice.Permanent);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(DeleteChoice.Cancel);
    }
}
