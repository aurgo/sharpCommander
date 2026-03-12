using Avalonia.Markup.Xaml;

namespace SharpCommander.Desktop.Views;

public partial class MassRenameWindow : Avalonia.Controls.Window
{
    public MassRenameWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
