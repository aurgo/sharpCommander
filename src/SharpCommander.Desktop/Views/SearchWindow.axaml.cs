using Avalonia.Markup.Xaml;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

public partial class SearchWindow : Avalonia.Controls.Window
{
    public SearchWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
