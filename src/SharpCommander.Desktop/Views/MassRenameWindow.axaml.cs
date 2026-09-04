using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Mass rename window; the preview grid shows the validation status of every new name.
/// </summary>
public partial class MassRenameWindow : Window
{
    public MassRenameWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
