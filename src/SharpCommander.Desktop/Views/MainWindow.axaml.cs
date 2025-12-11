using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        Closing += OnClosing;
        
        // Attach drag-drop handlers to favorites list
        if (this.FindControl<ListBox>("FavoritesListBox") is ListBox favListBox)
        {
            favListBox.AddHandler(DragDrop.DropEvent, FavoritesList_Drop);
            favListBox.AddHandler(DragDrop.DragOverEvent, FavoritesList_DragOver);
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SaveStateAsync();
        }
    }

    private void LeftPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetActivePanel(viewModel.LeftPanel);
        }
    }

    private void RightPanel_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetActivePanel(viewModel.RightPanel);
        }
    }

    private void Favorites_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && 
            listBox.SelectedItem is FavoriteItem favorite &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ActivePanel?.NavigateToFavoriteCommand.Execute(favorite);
            listBox.SelectedItem = null; // Reset selection
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var aboutDialog = new AboutWindow
        {
            DataContext = new AboutViewModel()
        };
        aboutDialog.ShowDialog(this);
    }

    private void ThemeLight_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private void ThemeDark_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private void ThemeDefault_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    // Favorites drag-drop fields
    private Point? _favDragStartPoint;
    private bool _favIsDragging;

    private void FavoriteItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _favDragStartPoint = e.GetPosition(this);
            _favIsDragging = false;
        }
    }

    private async void FavoriteItem_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_favDragStartPoint.HasValue && 
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && 
            !_favIsDragging &&
            sender is StackPanel stackPanel &&
            stackPanel.DataContext is FavoriteItem favorite)
        {
            var currentPoint = e.GetPosition(this);
            var diff = _favDragStartPoint.Value - currentPoint;
            
            // Check if the pointer has moved enough to start dragging
            if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
            {
                _favIsDragging = true;
                
                // Create data object with favorite
                var dataObject = new DataObject();
                dataObject.Set("FavoriteItem", favorite);
                
                // Start drag operation
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Move);
                
                _favDragStartPoint = null;
                _favIsDragging = false;
            }
        }
    }

    private void FavoritesList_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("FavoriteItem"))
        {
            e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void FavoritesList_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!e.Data.Contains("FavoriteItem"))
        {
            return;
        }

        var draggedItem = e.Data.Get("FavoriteItem") as FavoriteItem;
        if (draggedItem == null)
        {
            return;
        }

        // Find the target item
        if (sender is ListBox listBox)
        {
            var position = e.GetPosition(listBox);
            var hitTest = listBox.InputHitTest(position);
            
            FavoriteItem? targetItem = null;
            if (hitTest is Visual visual)
            {
                var item = visual.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is FavoriteItem favorite)
                {
                    targetItem = favorite;
                }
            }

            // Reorder the favorites
            var favorites = viewModel.LeftPanel.Favorites;
            var draggedIndex = favorites.IndexOf(draggedItem);
            
            if (draggedIndex >= 0)
            {
                favorites.RemoveAt(draggedIndex);
                
                if (targetItem != null)
                {
                    var targetIndex = favorites.IndexOf(targetItem);
                    if (targetIndex >= 0)
                    {
                        favorites.Insert(targetIndex, draggedItem);
                    }
                    else
                    {
                        favorites.Add(draggedItem);
                    }
                }
                else
                {
                    favorites.Add(draggedItem);
                }

                // Update order property
                for (int i = 0; i < favorites.Count; i++)
                {
                    favorites[i].Order = i;
                }

                // Save the updated order
                await viewModel.SaveStateAsync();
                
                // Reload favorites to sync
                viewModel.LeftPanel.LoadFavorites();
                viewModel.RightPanel.LoadFavorites();
            }
        }
    }
}
