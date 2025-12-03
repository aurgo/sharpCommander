using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SharpCommander.Desktop.Views;

public partial class HashWindow : Window
{
    public HashWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
