using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Properties window bound to a <see cref="PropertiesViewModel"/>. Closing cancels a running folder measurement.
/// </summary>
public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as PropertiesViewModel)?.Cancel();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
