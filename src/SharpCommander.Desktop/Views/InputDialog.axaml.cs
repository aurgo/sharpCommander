using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SharpCommander.Desktop.Views;

public partial class InputDialog : Window
{
    public string Result { get; private set; } = string.Empty;

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string initialValue = "") : this()
    {
        Title = title;
        var promptText = this.FindControl<TextBlock>("PromptText");
        if (promptText != null) promptText.Text = prompt;
        
        var inputBox = this.FindControl<TextBox>("InputBox");
        if (inputBox != null) inputBox.Text = initialValue;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var inputBox = this.FindControl<TextBox>("InputBox");
        Result = inputBox?.Text ?? string.Empty;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
