using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SharpCommander.Desktop.Views;

public partial class PropertiesWindow : Avalonia.Controls.Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
