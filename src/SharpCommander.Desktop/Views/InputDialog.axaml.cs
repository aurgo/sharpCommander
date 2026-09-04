using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Asks for one line of text. The optional validator runs on every change: its message is shown under
/// the box and OK stays disabled until the value is acceptable. Enter confirms, Escape cancels.
/// </summary>
public partial class InputDialog : Window
{
    private readonly Func<string, string?>? _validate;

    /// <summary>The accepted text; empty when the dialog was cancelled.</summary>
    public string Result { get; private set; } = string.Empty;

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string initialValue = "", Func<string, string?>? validate = null)
        : this()
    {
        Title = title;
        PromptText.Text = prompt;
        _validate = validate;
        InputBox.Text = initialValue;
        InputBox.PropertyChanged += OnInputPropertyChanged;
        Validate();

        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnInputPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            Validate();
        }
    }

    private void Validate()
    {
        var error = _validate?.Invoke(InputBox.Text ?? string.Empty);
        ErrorText.Text = error ?? string.Empty;
        ErrorText.IsVisible = error is not null;
        OkButton.IsEnabled = error is null;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (!OkButton.IsEnabled)
        {
            return;
        }

        Result = InputBox.Text ?? string.Empty;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
