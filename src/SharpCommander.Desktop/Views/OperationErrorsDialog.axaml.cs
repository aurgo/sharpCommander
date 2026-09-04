using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Lists every item that failed in a batch operation (path and reason) with a "Copy all" button.
/// </summary>
public partial class OperationErrorsDialog : Window
{
    private readonly IReadOnlyList<FileOperationError> _errors = [];

    public OperationErrorsDialog()
    {
        InitializeComponent();
    }

    public OperationErrorsDialog(string title, IReadOnlyList<FileOperationError> errors)
        : this()
    {
        ArgumentNullException.ThrowIfNull(errors);

        _errors = errors;
        Title = title;
        SummaryText.Text = errors.Count == 1
            ? "1 item could not be processed:"
            : $"{errors.Count:N0} items could not be processed:";
        ErrorsList.ItemsSource = errors;

        Opened += (_, _) => Dispatcher.UIThread.Post(() => CloseButton.Focus(), DispatcherPriority.Input);
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                var builder = new StringBuilder();
                foreach (var error in _errors)
                {
                    builder.Append(error.Path).Append(": ").AppendLine(error.Message);
                }

                await clipboard.SetTextAsync(builder.ToString());
            }
        }
        catch (Exception)
        {
            // Copying is a convenience; never turn its failure into another error.
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
