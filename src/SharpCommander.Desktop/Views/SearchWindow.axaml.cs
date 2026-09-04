using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Advanced search window. Double-click or Enter on a result activates it (the owner closes the window and
/// navigates to the entry); closing the window cancels a running search.
/// </summary>
public partial class SearchWindow : Window
{
    public SearchWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as SearchViewModel)?.Cancel();
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            PatternBox.Focus();
            PatternBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ActivateSelectedResult();
    }

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelectedResult();
            e.Handled = true;
        }
    }

    private void ActivateSelectedResult()
    {
        if (DataContext is SearchViewModel viewModel && viewModel.ActivateResultCommand.CanExecute(null))
        {
            viewModel.ActivateResultCommand.Execute(null);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
