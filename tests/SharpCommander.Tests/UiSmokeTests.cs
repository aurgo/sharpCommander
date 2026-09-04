using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.ViewModels;
using SharpCommander.Desktop.Views;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

public class UiSmokeTests
{
    [AvaloniaFact]
    public void MassRenameWindow_DataGridHasThemeAndTemplate()
    {
        var vm = new MassRenameViewModel(new FileSystemService(), new[] { "/tmp/a.txt", "/tmp/b.txt" });
        var win = new MassRenameWindow { DataContext = vm };
        win.Show();
        Dispatcher.UIThread.RunJobs();

        var grid = win.GetLogicalDescendants().OfType<DataGrid>().FirstOrDefault();
        Assert.NotNull(grid);
        Assert.True(Application.Current!.TryGetResource(typeof(DataGrid), null, out _), "DataGrid ControlTheme must be registered in App.axaml");
        Assert.NotNull(grid.Template);
    }

    [AvaloniaFact]
    public async Task SelectAll_UpdatesListBoxSelection()
    {
        using var dir = new TempDir();
        for (var i = 0; i < 5; i++) dir.File($"f{i}.txt");

        var fileSystem = new FileSystemService();
        var dialogs = new FakeDialogService();
        var trash = new FakeTrashService();
        var mainVm = new MainWindowViewModel(fileSystem, new FakeSettingsService(), dialogs, new ClipboardService(), new FileOperationsService(fileSystem, dialogs, trash), trash, new ThemeService());
        var panel = mainVm.LeftPanel;
        var view = new FilePanelView { DataContext = panel };
        var win = new Window { Content = view, Width = 600, Height = 400 };
        win.Show();
        await panel.InitializeAsync(dir.Path);
        Dispatcher.UIThread.RunJobs();

        mainVm.SetActivePanel(panel);
        mainVm.SelectAllCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var listBox = view.FindControl<ListBox>("FileListBox")!;
        Assert.Equal(5, panel.SelectedEntries.Count);
        Assert.Equal(5, listBox.SelectedItems?.Count);
    }
}
