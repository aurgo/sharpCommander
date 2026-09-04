using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Resolves a name collision during copy or move. Bound to a <see cref="ConflictDialogViewModel"/>;
/// the owner closes the window with the view model's result when it raises CloseRequested. Enter skips
/// (the safe default), Enter inside the rename box renames, and Escape cancels the whole operation.
/// </summary>
public partial class ConflictDialog : Window
{
    public ConflictDialog()
    {
        InitializeComponent();
        NewNameBox.KeyDown += NewNameBox_KeyDown;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => SkipButton.Focus(), DispatcherPriority.Input);
    }

    private void NewNameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ConflictDialogViewModel viewModel)
        {
            if (viewModel.RenameCommand.CanExecute(null))
            {
                viewModel.RenameCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
