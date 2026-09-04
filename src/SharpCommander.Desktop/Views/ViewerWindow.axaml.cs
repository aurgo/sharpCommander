using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SharpCommander.Desktop.ViewModels;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Read-only viewer window (F3). Escape closes, Ctrl+F (Cmd+F on macOS) focuses the find box, Enter in the
/// find box and F3 jump to the next match.
/// </summary>
public partial class ViewerWindow : Window
{
    private ViewerViewModel? _viewModel;

    public ViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.MatchFound -= OnMatchFound;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        if (DataContext is ViewerViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.MatchFound += OnMatchFound;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyWordWrap(viewModel.WordWrap);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.WordWrap) && _viewModel is not null)
        {
            ApplyWordWrap(_viewModel.WordWrap);
        }
    }

    private void ApplyWordWrap(bool wrap)
    {
        TextView.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    private void OnMatchFound(object? sender, int index)
    {
        if (_viewModel is null)
        {
            return;
        }

        TextView.SelectionStart = index;
        TextView.SelectionEnd = index + _viewModel.MatchLength;
        TextView.CaretIndex = index + _viewModel.MatchLength;
        TextView.Focus();
    }

    private void FindBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel is not null)
        {
            _viewModel.FindNextCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var isFindShortcut = e.Key == Key.F && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (isFindShortcut)
        {
            FindBox.Focus();
            FindBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && _viewModel is not null)
        {
            _viewModel.FindNextCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
