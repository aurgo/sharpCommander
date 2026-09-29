using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Coordinates the dual-pane file manager: tabs (each owning its two panels), the commands behind the function
/// keys, menus and shortcuts, the clipboard, favorites, theme and the status bar. Every file operation goes through
/// <see cref="IFileOperationsService"/>, so confirmation, progress and error reporting are the same everywhere.
/// Commands never throw: failures are logged and shown in an error dialog.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileOperationsService _operations;
    private readonly ITrashService _trashService;
    private readonly ThemeService _themeService;
    private readonly IArchiveService _archiveService;
    private readonly IDirectoryComparer _directoryComparer;

    /// <summary>Cancels the running folder comparison; null when none is running.</summary>
    private CancellationTokenSource? _comparison;
    private readonly IUndoService _undoService;
    private readonly ISftpConnections _connections;
    private readonly IUpdateService _updates;
    private readonly ISpaceAnalyzerService _spaceAnalyzer;
    private readonly ISecretStore _secrets = new KeychainSecretStore();

    /// <summary>Cancelled when the window goes away; stops the watch for new releases.</summary>
    private readonly CancellationTokenSource _lifetime = new();
    private bool _watchingForUpdates;
    private bool _disposed;

    [ObservableProperty]
    private TabViewModel? _currentTab;

    /// <summary>The tab each pane is showing right now. Switching tabs only replaces the one of its own side.</summary>
    private TabViewModel _currentLeftTab;
    private TabViewModel _currentRightTab;

    [ObservableProperty]
    private FilePanelViewModel _leftPanel;

    [ObservableProperty]
    private FilePanelViewModel _rightPanel;

    [ObservableProperty]
    private FilePanelViewModel? _activePanel;

    /// <summary>The last status line, prefixed with the time it was set.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesPanel = true;

    /// <summary>True while the two panes are being compared, so the status bar can offer to stop it.</summary>
    [ObservableProperty]
    private bool _isComparing;

    /// <summary>Whether to look for new releases automatically; mirrored into the settings.</summary>
    [ObservableProperty]
    private bool _checkForUpdates = true;

    /// <summary>The theme in use: "System", "Light" or "Dark".</summary>
    [ObservableProperty]
    private string _currentTheme = ThemeService.SystemTheme;

    /// <summary>The interface language: a culture name or "System".</summary>
    [ObservableProperty]
    private string _currentLanguage = "System";

    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IFileOperationsService fileOperationsService,
        ITrashService trashService,
        ThemeService themeService, IArchiveService archiveService, IDirectoryComparer directoryComparer, IUndoService undoService, ISftpConnections connections, IUpdateService updates, ISpaceAnalyzerService spaceAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(fileOperationsService);
        ArgumentNullException.ThrowIfNull(trashService);
        ArgumentNullException.ThrowIfNull(themeService);

        _fileSystemService = fileSystemService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _operations = fileOperationsService;
        _trashService = trashService;
        _themeService = themeService;
        _archiveService = archiveService;
        _directoryComparer = directoryComparer;
        _undoService = undoService;
        _connections = connections;
        _updates = updates;
        _spaceAnalyzer = spaceAnalyzer;
        _undoService.Changed += (_, _) => OnPropertyChanged(nameof(UndoDescription));

        _operations.OperationCompleted += OnOperationCompleted;

        _leftPanel = CreatePanel();
        _rightPanel = CreatePanel();
        _activePanel = _leftPanel;

        _leftPanel.FavoritesChanged += OnFavoritesChanged;
        _rightPanel.FavoritesChanged += OnFavoritesChanged;
        _leftPanel.PropertyChanged += OnLeftPanelPropertyChanged;
        _rightPanel.PropertyChanged += OnRightPanelPropertyChanged;

        // Each pane starts with one tab of its own, so both sides always have somewhere to come back to.
        _currentLeftTab = new TabViewModel(PanelSide.Left);
        _currentRightTab = new TabViewModel(PanelSide.Right);
        AddTab(_currentLeftTab);
        AddTab(_currentRightTab);
        CurrentTab = _currentLeftTab;
    }

    public string Title => "SharpCommander - File Manager";

    /// <summary>The application version as built (single-sourced from Directory.Build.props).</summary>
    public string Version { get; } = ReadVersion();

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>
    /// The same tabs split by the side they belong to. The tab bar shows the left ones pinned to its left edge
    /// and the right ones pinned to its right edge, so which pane a tab drives is visible without opening it.
    /// </summary>
    public ObservableCollection<TabViewModel> LeftTabs { get; } = [];

    public ObservableCollection<TabViewModel> RightTabs { get; } = [];

    /// <summary>State of the running batch operation, for the status bar (progress, current file, cancel).</summary>
    public IFileOperationsService Operations => _operations;

    /// <summary>True when deleting can go to the operating system trash.</summary>
    public bool IsTrashAvailable => _trashService.IsSupported;

    /// <summary>The theme names, in menu order.</summary>
    public IReadOnlyList<string> Themes => ThemeService.Themes;

    /// <summary>Window width restored from the settings after <see cref="InitializeAsync"/>, null when unknown.</summary>
    public double? SavedWindowWidth { get; private set; }

    /// <summary>Window height restored from the settings after <see cref="InitializeAsync"/>, null when unknown.</summary>
    public double? SavedWindowHeight { get; private set; }

    /// <summary>Whether the window was maximized when the settings were saved.</summary>
    public bool SavedWindowMaximized { get; private set; }

    /// <summary>Raised by <see cref="ExitCommand"/>; the window closes itself in response.</summary>
    public event EventHandler? ExitRequested;

    // ---- lifecycle --------------------------------------------------------------------------------------

    /// <summary>Loads the settings, applies the theme and navigates the first tab to the saved folders.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _settingsService.LoadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be loaded.", ex);
            await _dialogService.ShowErrorAsync("Settings", $"The settings could not be loaded: {ex.Message}", ex.ToString());
        }

        var settings = _settingsService.Settings;
        ApplyTheme(settings.Theme);
        Localization.Strings.Use(settings.Language);
        CurrentLanguage = settings.Language;

        // The window was built and measured in English; the setting has just switched the texts. The property
        // above usually holds the same value it started with ("System"), so nothing was raised and the views
        // would keep the widths of the old words — this says so explicitly.
        OnPropertyChanged(nameof(CurrentLanguage));
        ShowFavoritesPanel = settings.FavoritesPanelVisible;
        SavedWindowWidth = settings.WindowWidth;
        SavedWindowHeight = settings.WindowHeight;
        SavedWindowMaximized = string.Equals(settings.WindowState, "Maximized", StringComparison.OrdinalIgnoreCase);

        var leftPath = settings.LastLeftPanelPath ?? _fileSystemService.GetDefaultDirectory();
        var rightPath = settings.LastRightPanelPath ?? _fileSystemService.GetDefaultDirectory();

        await RunGuardedAsync("Startup", () => Task.WhenAll(
            LeftPanel.InitializeAsync(leftPath),
            RightPanel.InitializeAsync(rightPath)));

        RestoreTabs(settings.Tabs);

        CheckForUpdates = settings.CheckForUpdates;

        // Fire and forget, once a day at most: a release check must never hold up the window.
        if (settings.CheckForUpdates && DueForCheck(settings.LastUpdateCheck, UpdateCheckInterval))
        {
            _ = RunGuardedAsync(Localization.Strings.Get("Update_Title"), () => CheckForUpdatesAsync(announceWhenUpToDate: false));
        }

        if (!_watchingForUpdates)
        {
            _watchingForUpdates = true;
            _ = WatchForUpdatesAsync(_lifetime.Token);
        }

        SetStatus("Ready.");
    }

    /// <summary>Records the window size for <see cref="SaveStateAsync"/>; call it when the window is resized or closing.</summary>
    public void RememberWindowBounds(double width, double height, bool isMaximized)
    {
        var settings = _settingsService.Settings;
        if (!isMaximized && width > 0 && height > 0)
        {
            settings.WindowWidth = width;
            settings.WindowHeight = height;
        }

        settings.WindowState = isMaximized ? "Maximized" : "Normal";
    }

    /// <summary>Copies the panel paths, favorites panel visibility and theme into the settings without saving.</summary>
    public void CaptureState()
    {
        if (_disposed)
        {
            // The window is gone and its tab collections were emptied by Dispose. A save still runs after that
            // — closing the last window raises ShutdownRequested once the window is already closed — and
            // capturing from the emptied collections wrote an empty tab list over the session just saved, which
            // is why every start came up with a single tab. What Dispose captured is what should be written.
            return;
        }

        var settings = _settingsService.Settings;
        settings.LastLeftPanelPath = LeftPanel.CurrentPath;
        settings.LastRightPanelPath = RightPanel.CurrentPath;
        settings.Tabs = CaptureTabs();
        settings.FavoritesPanelVisible = ShowFavoritesPanel;
        settings.Theme = _themeService.CurrentTheme;
        settings.Language = CurrentLanguage;
    }

    /// <summary>Captures the state and writes the settings now. Safe to block on: it never resumes on the UI thread.</summary>
    public Task SaveStateAsync()
    {
        CaptureState();
        return _settingsService.SaveAsync();
    }

    /// <summary>
    /// Asks whether to close while a copy, move or delete is still running. Returns true when there is nothing
    /// running or the user confirmed; false to keep the window open. The batch itself is stopped by
    /// <see cref="ShutdownAsync"/>, which cancels it and waits for the engine to clean up.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!_operations.IsRunning)
        {
            return true;
        }

        return await _dialogService.ShowConfirmAsync(
            "An operation is running",
            "A file operation is still running. Cancel it and quit?",
            confirmText: "Cancel and quit",
            cancelText: "Keep working",
            destructive: true,
            // Losing an in-flight copy to a stray Enter is not recoverable; keep working is the armed answer.
            defaultIsCancel: true);
    }

    /// <summary>Saves the state and flushes any pending settings write; await it before closing the window.</summary>
    public async Task ShutdownAsync()
    {
        // A copy or move still writing to disk is cancelled and awaited first: the engine removes the
        // partially written destination file, so exiting never leaves a truncated file under its final name.
        try
        {
            if (_operations.IsRunning)
            {
                _operations.Cancel();
                await _operations.WhenIdleAsync().WaitAsync(ShutdownGracePeriod);
            }
        }
        catch (TimeoutException)
        {
            AppLog.Warning("A file operation did not stop within the shutdown grace period.");
        }
        catch (Exception ex)
        {
            AppLog.Error("The running file operation could not be stopped on exit.", ex);
        }

        try
        {
            await SaveStateAsync();
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be saved on exit.", ex);
        }
    }

    /// <summary>How long the window waits for a running batch to stop before it closes anyway.</summary>
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>Makes a panel of the current tab the active one.</summary>
    public void SetActivePanel(FilePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (!ReferenceEquals(panel, LeftPanel) && !ReferenceEquals(panel, RightPanel))
        {
            return;
        }

        ActivePanel = panel;

        // Highlight that pane's tab, but do not request focus: this is called from the focus handlers.
        CurrentTab = CurrentTabOf(SideOf(panel));
    }

    /// <summary>
    /// Handles files dropped on <paramref name="target"/>: moves them when <paramref name="move"/>, else copies them,
    /// through the operations service like every other command.
    /// </summary>
    public Task DropAsync(FilePanelViewModel target, IReadOnlyList<string> paths, bool move)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paths);

        return RunGuardedAsync(move ? "Move" : "Copy", async () =>
        {
            if (paths.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(target.CurrentPath))
            {
                SetStatus("Files cannot be dropped on the Computer view.");
                return;
            }

            var result = move
                ? await _operations.MoveAsync(paths, target.CurrentPath)
                : await _operations.CopyAsync(paths, target.CurrentPath);

            if (result.Started)
            {
                await RefreshPanelsAsync(LeftPanel, RightPanel);
            }
        });
    }

    partial void OnCurrentTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsCurrent = false;
        }

        if (newValue is not null)
        {
            newValue.IsCurrent = true;
        }
    }

    // ---- tabs: one shared pair of panels, a tab is a folder remembered for one side ---------------------

    /// <summary>
    /// Rebuilds the tab bar from a saved session. The tab each pane starts with is reused for the first restored
    /// tab of that side, so the panes keep the folders they were just navigated to; anything left over is added.
    /// A saved tab whose folder has since disappeared is dropped rather than restored onto a dead path.
    /// </summary>
    private void RestoreTabs(IReadOnlyList<TabState>? saved)
    {
        if (saved is null || saved.Count == 0)
        {
            return;
        }

        foreach (var side in new[] { PanelSide.Left, PanelSide.Right })
        {
            var states = saved
                .Where(state => ParseSide(state.Side) == side)
                .Where(state => state.Path.Length == 0 || _fileSystemService.IsDirectory(state.Path))
                .ToList();

            if (states.Count == 0)
            {
                continue;
            }

            var group = GroupOf(side);
            var placeholder = group[0];
            var current = placeholder;

            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                TabViewModel tab;

                if (index == 0)
                {
                    // Reuse the tab the pane already shows so no navigation is needed for it.
                    placeholder.Path = state.Path;
                    placeholder.IsPinned = state.IsPinned;
                    tab = placeholder;
                }
                else
                {
                    tab = new TabViewModel(side, state.Path) { IsPinned = state.IsPinned };
                    AddTab(tab);
                }

                if (state.IsCurrent)
                {
                    current = tab;
                }
            }

            // The pane is already on the placeholder's folder; only a different current tab needs a navigation.
            if (!ReferenceEquals(current, placeholder))
            {
                _ = ActivateAsync(current);
            }
            else
            {
                placeholder.Path = PathOf(PanelOf(side));
            }
        }
    }

    private List<TabState> CaptureTabs()
    {
        return Tabs
            .Select(tab => new TabState
            {
                Side = tab.Side.ToString(),
                // The tab a pane is showing is only as up to date as its last navigation, so read the pane itself.
                Path = ReferenceEquals(tab, CurrentTabOf(tab.Side)) ? PathOf(PanelOf(tab.Side)) : tab.Path,
                IsPinned = tab.IsPinned,
                IsCurrent = ReferenceEquals(tab, CurrentTabOf(tab.Side))
            })
            .ToList();
    }

    private static PanelSide ParseSide(string? side)
    {
        return string.Equals(side, nameof(PanelSide.Right), StringComparison.OrdinalIgnoreCase)
            ? PanelSide.Right
            : PanelSide.Left;
    }

    /// <summary>The panel of a side.</summary>
    private FilePanelViewModel PanelOf(PanelSide side) => side == PanelSide.Left ? LeftPanel : RightPanel;

    private PanelSide SideOf(FilePanelViewModel panel) => ReferenceEquals(panel, RightPanel) ? PanelSide.Right : PanelSide.Left;

    /// <summary>The pane opposite to <paramref name="panel"/> (the left one for anything else).</summary>
    private FilePanelViewModel OtherPanel(FilePanelViewModel panel) => ReferenceEquals(panel, LeftPanel) ? RightPanel : LeftPanel;

    private ObservableCollection<TabViewModel> GroupOf(PanelSide side) => side == PanelSide.Left ? LeftTabs : RightTabs;

    private TabViewModel CurrentTabOf(PanelSide side) => side == PanelSide.Left ? _currentLeftTab : _currentRightTab;

    /// <summary>
    /// Makes <paramref name="tab"/> the one its pane is showing. The folder the pane is leaving is written back
    /// into the tab it belonged to, and only this side navigates: the opposite pane is never touched.
    /// </summary>
    private async Task ActivateAsync(TabViewModel tab)
    {
        var panel = PanelOf(tab.Side);
        var leaving = CurrentTabOf(tab.Side);

        if (!ReferenceEquals(leaving, tab))
        {
            leaving.Path = PathOf(panel);

            SetCurrentTabOf(tab.Side, tab);

            if (!string.Equals(PathOf(panel), tab.Path, StringComparison.Ordinal))
            {
                await panel.NavigateToAsync(tab.Path);
            }
        }

        CurrentTab = tab;
        ActivePanel = panel;
        panel.RequestFocus();
    }

    /// <summary>The folder a panel is showing, with the volume list written as the empty path.</summary>
    private static string PathOf(FilePanelViewModel panel) => panel.IsRootView ? string.Empty : panel.CurrentPath;

    private void OnLeftPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TrackPanelFolder(LeftPanel, _currentLeftTab, e);
    }

    private void OnRightPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        TrackPanelFolder(RightPanel, _currentRightTab, e);
    }

    /// <summary>Keeps the tab a pane is showing named after the folder that pane navigates to.</summary>
    private void TrackPanelFolder(FilePanelViewModel panel, TabViewModel tab, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(FilePanelViewModel.CurrentPath) or nameof(FilePanelViewModel.IsRootView)))
        {
            return;
        }

        var path = PathOf(panel);
        if (!tab.IsPinned)
        {
            tab.Path = path;
            return;
        }

        // The pane wandered off a pinned tab: give the new folder a tab of its own and leave the pin alone.
        if (!string.Equals(tab.Path, path, StringComparison.Ordinal))
        {
            var spawned = new TabViewModel(tab.Side, path);
            AddTab(spawned);
            SetCurrentTabOf(tab.Side, spawned);
            CurrentTab = spawned;
        }
    }

    private void SetCurrentTabOf(PanelSide side, TabViewModel tab)
    {
        if (side == PanelSide.Left)
        {
            _currentLeftTab = tab;
        }
        else
        {
            _currentRightTab = tab;
        }
    }

    // ---- file operations ------------------------------------------------------------------------------

    [RelayCommand]
    private Task CopyAsync() => TransferToOtherPanelAsync(FileOperationKind.Copy);

    [RelayCommand]
    private Task MoveAsync() => TransferToOtherPanelAsync(FileOperationKind.Move);

    [RelayCommand]
    private Task DeleteAsync() => DeleteSelectedAsync(permanentRequested: false);

    [RelayCommand]
    private Task DeletePermanentAsync() => DeleteSelectedAsync(permanentRequested: true);

    [RelayCommand]
    private Task RenameAsync() => RunGuardedAsync("Rename", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (panel.SelectedEntry is not { } entry || entry.EntryType is FileSystemEntryType.ParentDirectory or FileSystemEntryType.Drive)
        {
            SetStatus("Select a file or folder to rename.");
            return;
        }

        var newPath = await _operations.RenameAsync(entry);
        if (newPath is null)
        {
            return;
        }

        _undoService.Record(new UndoAction
        {
            Kind = UndoActionKind.Rename,
            Description = $"rename of '{Path.GetFileName(newPath)}'",
            Items = [new UndoItem(entry.FullPath, newPath)]
        });

        await panel.RefreshAsync();
        SelectByPath(panel, newPath);
        panel.RequestFocus();
        SetStatus($"Renamed '{entry.Name}' to '{Path.GetFileName(newPath)}'.");
    });

    [RelayCommand]
    private Task NewFolderAsync() => RunGuardedAsync("New Folder", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (string.IsNullOrEmpty(panel.CurrentPath))
        {
            SetStatus("Folders cannot be created in the Computer view.");
            return;
        }

        var created = await _operations.CreateDirectoryAsync(panel.CurrentPath);
        if (created is null)
        {
            return;
        }

        _undoService.Record(new UndoAction
        {
            Kind = UndoActionKind.CreateFolder,
            Description = $"creation of '{Path.GetFileName(created)}'",
            Items = [new UndoItem(string.Empty, created)]
        });

        await panel.RefreshAsync();
        SelectByPath(panel, created);
        panel.RequestFocus();
        SetStatus($"Created folder '{Path.GetFileName(created)}'.");
    });

    [RelayCommand]
    private Task ViewAsync() => RunGuardedAsync("View", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to view.");
            return;
        }

        await _dialogService.ShowViewerAsync(entry.FullPath);
    });

    [RelayCommand]
    private Task EditAsync() => RunGuardedAsync("Edit", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to edit.");
            return;
        }

        if (await _operations.OpenAsync(entry))
        {
            SetStatus($"Opened '{entry.Name}'.");
        }
    });

    [RelayCommand]
    private Task RefreshAsync() => RunGuardedAsync("Refresh", () => RefreshPanelsAsync(LeftPanel, RightPanel));

    private Task TransferToOtherPanelAsync(FileOperationKind kind)
    {
        var title = kind == FileOperationKind.Copy ? "Copy" : "Move";
        return RunGuardedAsync(title, async () =>
        {
            if (ActivePanel is not { } source || CurrentTab is not { } tab)
            {
                return;
            }

            var items = source.GetSelectedItems();
            if (items.Count == 0)
            {
                SetStatus("Nothing selected.");
                return;
            }

            var target = OtherPanel(source);
            if (string.IsNullOrEmpty(target.CurrentPath))
            {
                SetStatus("Open a destination folder in the other panel first.");
                return;
            }

            var anchor = source.FilteredEntries.IndexOf(items[0]);
            var paths = items.Select(item => item.FullPath).ToList();

            // Which destination names did not exist before: undoing a copy may only remove what the copy made.
            var newNames = items
                .Where(item => !Exists(Path.Combine(target.CurrentPath, item.Name)))
                .Select(item => item.Name)
                .ToHashSet(PathUtils.PathComparer);

            var result = kind == FileOperationKind.Copy
                ? await _operations.CopyAsync(paths, target.CurrentPath)
                : await _operations.MoveAsync(paths, target.CurrentPath);

            if (!result.Started)
            {
                return;
            }

            RecordTransfer(kind, items, target.CurrentPath, newNames);

            if (kind == FileOperationKind.Copy)
            {
                await RefreshPanelsAsync(target);
            }
            else
            {
                await RefreshPanelsAsync(source, target);
                SelectAt(source, anchor);
            }

            source.RequestFocus();
        });
    }

    private Task DeleteSelectedAsync(bool permanentRequested) => RunGuardedAsync("Delete", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        var anchor = panel.FilteredEntries.IndexOf(items[0]);
        var result = await _operations.DeleteAsync(items, permanentRequested);
        if (!result.Started)
        {
            return;
        }

        await RefreshPanelsAsync(panel);
        SelectAt(panel, anchor);
        panel.RequestFocus();
    });

    // ---- clipboard --------------------------------------------------------------------------------------

    [RelayCommand]
    private Task CopyToClipboardAsync() => RunGuardedAsync("Copy", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        await _clipboardService.CopyAsync(items);
        SetStatus($"Copied {Plural(items.Count, "item")} to the clipboard.");
    });

    [RelayCommand]
    private Task CutToClipboardAsync() => RunGuardedAsync("Cut", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var items = panel.GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Nothing selected.");
            return;
        }

        await _clipboardService.CutAsync(items);
        SetStatus($"Cut {Plural(items.Count, "item")} to the clipboard.");
    });

    [RelayCommand]
    private Task PasteFromClipboardAsync() => RunGuardedAsync("Paste", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        if (string.IsNullOrEmpty(panel.CurrentPath))
        {
            SetStatus("Files cannot be pasted in the Computer view.");
            return;
        }

        var paths = await _clipboardService.GetPathsAsync();
        if (paths.Count == 0)
        {
            SetStatus("The clipboard holds no files.");
            return;
        }

        var move = _clipboardService.IsCutMode;
        var result = move
            ? await _operations.MoveAsync(paths, panel.CurrentPath)
            : await _operations.CopyAsync(paths, panel.CurrentPath);

        if (!result.Started)
        {
            return;
        }

        if (move && !result.Cancelled)
        {
            // The cut files are gone from their source; the clipboard must not move them again.
            await _clipboardService.ClearAsync();
        }

        await RefreshPanelsAsync(LeftPanel, RightPanel);
        panel.RequestFocus();
    });

    // ---- selection, panels, tabs ----------------------------------------------------------------------

    [RelayCommand]
    private void SelectAll()
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        panel.SelectedEntries.Clear();
        foreach (var entry in panel.FilteredEntries)
        {
            if (entry.EntryType != FileSystemEntryType.ParentDirectory)
            {
                panel.SelectedEntries.Add(entry);
            }
        }

        SetStatus($"Selected {Plural(panel.SelectedEntries.Count, "item")}.");
    }

    [RelayCommand]
    private void ToggleFavoritesPanel()
    {
        ShowFavoritesPanel = !ShowFavoritesPanel;
        _settingsService.Settings.FavoritesPanelVisible = ShowFavoritesPanel;
        _settingsService.RequestSave();
    }

    [RelayCommand]
    private Task ToggleFavoriteAsync() => RunGuardedAsync("Favorites", async () =>
    {
        if (ActivePanel is { } panel)
        {
            await panel.ToggleFavoriteCommand.ExecuteAsync(null);
        }
    });

    [RelayCommand]
    private void ToggleSearch()
    {
        ActivePanel?.ToggleSearchCommand.Execute(null);
    }

    [RelayCommand]
    private Task SyncPanelsAsync() => RunGuardedAsync("Sync panels", async () =>
    {
        if (ActivePanel is not { } source)
        {
            return;
        }

        await OtherPanel(source).NavigateToCommand.ExecuteAsync(source.CurrentPath);
    });

    /// <summary>Switches the interface language. Every bound text updates at once; no restart is needed.</summary>
    [RelayCommand]
    private void SetLanguage(string? language)
    {
        var wanted = string.IsNullOrWhiteSpace(language) ? "System" : language;

        Localization.Strings.Use(wanted);
        CurrentLanguage = wanted;
        _settingsService.Settings.Language = wanted;
        _settingsService.RequestSave();
    }

    /// <summary>The languages on offer, for the menu.</summary>
    public IReadOnlyList<Localization.LanguageOption> Languages => Localization.Strings.Available;

    // ---- undo -------------------------------------------------------------------------------------------

    /// <summary>What the next undo would reverse, for the menu; empty when there is nothing to undo.</summary>
    public string UndoDescription => _undoService.Next is { } action ? $"Undo {action.Description}" : "Undo";

    /// <summary>
    /// Reverses the last copy, move, rename or folder creation. Deletion is never on the stack: the platform
    /// trash has no restore API, so an undo entry for it could not keep its promise.
    /// </summary>
    [RelayCommand]
    private Task UndoAsync() => RunGuardedAsync("Undo", async () =>
    {
        if (_undoService.Next is not { } action)
        {
            SetStatus("There is nothing to undo.");
            return;
        }

        // Undoing a copy or a folder creation deletes things, so it is confirmed like any other deletion.
        if (action.IsDestructive)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                "Undo",
                $"Undoing the {action.Description} will delete {action.Items.Count} item{(action.Items.Count == 1 ? string.Empty : "s")} that were created.\n\nContinue?",
                "Delete",
                "Cancel",
                destructive: true);

            if (!confirmed)
            {
                return;
            }
        }

        _undoService.Take();

        var done = 0;
        var skipped = 0;

        foreach (var item in action.Items)
        {
            switch (action.Kind)
            {
                case UndoActionKind.Move or UndoActionKind.Rename:
                    // Never overwrite: if something has taken the old name since, leave both alone.
                    if (!Exists(item.To) || Exists(item.From))
                    {
                        skipped++;
                        continue;
                    }

                    // A move goes back to the folder it came from; a rename keeps the folder and restores the
                    // name. They are different calls: MoveAsync takes a destination folder, not a full path.
                    if (action.Kind == UndoActionKind.Rename)
                    {
                        await _fileSystemService.RenameAsync(item.To, Path.GetFileName(item.From));
                    }
                    else
                    {
                        await _fileSystemService.MoveAsync(item.To, Path.GetDirectoryName(item.From)!);
                    }

                    done++;
                    break;

                case UndoActionKind.Copy or UndoActionKind.CreateFolder:
                    if (!Exists(item.To))
                    {
                        skipped++;
                        continue;
                    }

                    await _fileSystemService.DeleteAsync(item.To);
                    done++;
                    break;
            }
        }

        await RefreshAsync();

        SetStatus(skipped == 0
            ? $"Undone: {action.Description}."
            : $"Undone: {action.Description} ({done} of {action.Items.Count}; {skipped} had changed since).");
    });

    /// <summary>Records a finished copy or move so it can be undone.</summary>
    private void RecordTransfer(FileOperationKind kind, IReadOnlyList<FileSystemEntry> items, string destination, IReadOnlySet<string> newNames)
    {
        var moved = kind == FileOperationKind.Move;

        var entries = items
            // A copy may only undo what it created; a move takes everything it moved.
            .Where(item => moved || newNames.Contains(item.Name))
            .Where(item => Exists(Path.Combine(destination, item.Name)))
            .Select(item => new UndoItem(item.FullPath, Path.Combine(destination, item.Name)))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        _undoService.Record(new UndoAction
        {
            Kind = moved ? UndoActionKind.Move : UndoActionKind.Copy,
            Description = $"{(moved ? "move" : "copy")} of {entries.Count} item{(entries.Count == 1 ? string.Empty : "s")}",
            Items = entries
        });
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    // ---- updates ----------------------------------------------------------------------------------------

    /// <summary>
    /// The newer release the last check found, or null. While it is set the menu bar shows a button that opens its
    /// download page, so a release put off with "Later" is not forgotten once its dialog is gone.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableVersion))]
    private UpdateInfo? _availableUpdate;

    /// <summary>The version of <see cref="AvailableUpdate"/> for display ("2.4.0"), or null when there is none.</summary>
    public string? AvailableVersion => AvailableUpdate?.Version.ToString();

    /// <summary>Looks for a newer release now, and says so either way.</summary>
    [RelayCommand]
    private Task CheckForUpdatesNowAsync() =>
        RunGuardedAsync(Localization.Strings.Get("Update_Title"), () => CheckForUpdatesAsync(announceWhenUpToDate: true));

    /// <summary>Opens the download page of the newer release in the browser.</summary>
    [RelayCommand]
    private Task OpenUpdatePageAsync() => RunGuardedAsync(Localization.Strings.Get("Update_Title"), async () =>
    {
        if (AvailableUpdate is { } update)
        {
            await _fileSystemService.OpenWithDefaultAsync(update.Url);
        }
    });

    /// <summary>Turns the automatic check on or off.</summary>
    [RelayCommand]
    private void ToggleUpdateChecks()
    {
        CheckForUpdates = !CheckForUpdates;
        _settingsService.Settings.CheckForUpdates = CheckForUpdates;
        _settingsService.RequestSave();
        SetStatus(Localization.Strings.Get(CheckForUpdates ? "Update_ChecksOn" : "Update_ChecksOff"));
    }

    /// <summary>
    /// Asks the update service and reports. The automatic check stays quiet unless there is something new to say,
    /// and asks about each release once: from then on the button in the menu bar is the reminder, rather than the
    /// same question every morning. The check from the menu answers either way, because the user asked a question
    /// and deserves an answer.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool announceWhenUpToDate)
    {
        var settings = _settingsService.Settings;
        settings.LastUpdateCheck = DateTime.UtcNow;
        _settingsService.RequestSave();

        if (!System.Version.TryParse(Version, out var current))
        {
            return;
        }

        var found = await _updates.CheckAsync(current);

        var title = Localization.Strings.Get("Update_Title");

        if (found is null)
        {
            SetStatus(Localization.Strings.Get("Update_Unreachable"));

            // Asked for by hand, so it gets an answer on screen: a line in the status bar looks like nothing
            // happened, which is exactly what the user would conclude.
            if (announceWhenUpToDate)
            {
                await _dialogService.ShowMessageAsync(title, Localization.Strings.Get("Update_UnreachableLong"));
            }

            return;
        }

        if (!found.IsNewer)
        {
            AvailableUpdate = null;
            SetStatus(Localization.Strings.Format("Update_Current", Version));

            if (announceWhenUpToDate)
            {
                await _dialogService.ShowMessageAsync(title, Localization.Strings.Format("Update_CurrentLong", Version));
            }

            return;
        }

        AvailableUpdate = found;
        SetStatus(Localization.Strings.Format("Update_Available", found.Tag));

        if (!announceWhenUpToDate && string.Equals(settings.LastAnnouncedUpdate, found.Tag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.LastAnnouncedUpdate = found.Tag;
        _settingsService.RequestSave();

        var open = await _dialogService.ShowConfirmAsync(
            title,
            Localization.Strings.Format("Update_AvailableLong", found.Tag, Version),
            Localization.Strings.Get("Update_Open"),
            Localization.Strings.Get("Update_Later"));

        if (open)
        {
            await _fileSystemService.OpenWithDefaultAsync(found.Url);
        }
    }

    /// <summary>
    /// Keeps looking while the window stays open, so a release published after startup is noticed without a
    /// restart: every <see cref="UpdateWatchInterval"/> the watch checks whether a day has passed since the last
    /// check, and runs one when it has.
    /// </summary>
    private async Task WatchForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(UpdateWatchInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (CheckForUpdates && DueForCheck(_settingsService.Settings.LastUpdateCheck, UpdateCheckInterval))
                {
                    await RunGuardedAsync(Localization.Strings.Get("Update_Title"), () => CheckForUpdatesAsync(announceWhenUpToDate: false));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The window has closed.
        }
    }

    /// <summary>How often the automatic check runs: at startup and while the window is open, once a day at most.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

    /// <summary>How often the open window looks at whether the next check is due. Tests shorten it.</summary>
    internal TimeSpan UpdateWatchInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>True when <paramref name="last"/> is at least <paramref name="interval"/> ago, or never happened.</summary>
    internal static bool DueForCheck(DateTime? last, TimeSpan interval) => last is not { } when || DateTime.UtcNow - when >= interval;

    // ---- SpaceAnalyzer ----------------------------------------------------------------------------------

    private const string SpaceAnalyzerTitle = "SpaceAnalyzer";

    /// <summary>
    /// How long opening a kept copy waits for GitHub to say whether there is a newer release. Past that (no network,
    /// a slow line) the kept copy opens as it is.
    /// </summary>
    private static readonly TimeSpan SpaceAnalyzerLookupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opens SpaceAnalyzer on the active pane's folder. The first time, the build for this system is downloaded from
    /// its GitHub release and kept. Every later time GitHub is asked briefly for a newer release: when there is one it
    /// is downloaded and opened right away, and when GitHub does not answer the kept copy opens, even offline.
    /// </summary>
    [RelayCommand]
    private Task OpenSpaceAnalyzerAsync() => RunGuardedAsync(SpaceAnalyzerTitle, async () =>
    {
        if (!_spaceAnalyzer.IsSupported)
        {
            await _dialogService.ShowMessageAsync(
                SpaceAnalyzerTitle,
                Localization.Strings.Format("SpaceAnalyzer_Unsupported", $"{RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}"));
            return;
        }

        var install = _spaceAnalyzer.Installed is { } kept
            ? await UpdateSpaceAnalyzerAsync(kept)
            : await DownloadSpaceAnalyzerAsync();

        if (install is null)
        {
            return;
        }

        // Only a folder on this computer can be scanned; anything else opens on the start screen, which lists the drives.
        var panel = ActivePanel;
        var folder = panel is { IsRootView: false, CurrentPath.Length: > 0 } && !AnyPath.IsRemote(panel.CurrentPath)
            ? panel.CurrentPath
            : null;

        await _spaceAnalyzer.LaunchAsync(install, folder);

        SetStatus(folder is not null
            ? Localization.Strings.Format("SpaceAnalyzer_Opened", folder)
            : Localization.Strings.Get(panel is not null && AnyPath.IsRemote(panel.CurrentPath) ? "SpaceAnalyzer_OpenedRemote" : "SpaceAnalyzer_OpenedStart"));
    });

    /// <summary>
    /// Shows the folder where the downloaded copies of SpaceAnalyzer are kept in the active pane, so they can be
    /// looked at, replaced or removed by hand.
    /// </summary>
    [RelayCommand]
    private Task ShowSpaceAnalyzerFolderAsync() => RunGuardedAsync(SpaceAnalyzerTitle, async () =>
    {
        var folder = _spaceAnalyzer.Folder;
        if (!Directory.Exists(folder))
        {
            await _dialogService.ShowMessageAsync(SpaceAnalyzerTitle, Localization.Strings.Format("SpaceAnalyzer_NoFolderYet", folder));
            return;
        }

        if (ActivePanel is { } panel)
        {
            await panel.NavigateToAsync(folder);
        }
    });

    /// <summary>
    /// Downloads SpaceAnalyzer for the first time, with the progress in the status bar. This is the one step that
    /// needs the network, so when it fails the user is told why; the result is then null.
    /// </summary>
    private async Task<SpaceAnalyzerInstall?> DownloadSpaceAnalyzerAsync()
    {
        SetStatus(Localization.Strings.Get("SpaceAnalyzer_Looking"));

        var release = await _spaceAnalyzer.FindLatestAsync();
        _settingsService.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow;
        _settingsService.RequestSave();

        if (release is null)
        {
            SetStatus(Localization.Strings.Get("SpaceAnalyzer_Unreachable"));
            await _dialogService.ShowMessageAsync(SpaceAnalyzerTitle, Localization.Strings.Get("SpaceAnalyzer_UnreachableLong"));
            return null;
        }

        try
        {
            return await InstallSpaceAnalyzerAsync(release);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error("SpaceAnalyzer could not be downloaded.", ex);
            SetStatus(Localization.Strings.Get("SpaceAnalyzer_Failed"));
            await _dialogService.ShowErrorAsync(SpaceAnalyzerTitle, Localization.Strings.Format("SpaceAnalyzer_FailedLong", ex.Message), ex.ToString());
            return null;
        }
    }

    /// <summary>
    /// Returns the copy to open: a newer release when GitHub has one and it downloads, otherwise the kept copy. The
    /// kept copy still works, so nothing here opens a dialog; a failed update is one line in the status bar.
    /// </summary>
    private async Task<SpaceAnalyzerInstall> UpdateSpaceAnalyzerAsync(SpaceAnalyzerInstall kept)
    {
        SpaceAnalyzerRelease? release;
        using (var lookup = new CancellationTokenSource(SpaceAnalyzerLookupTimeout))
        {
            try
            {
                release = await _spaceAnalyzer.FindLatestAsync(lookup.Token);
            }
            catch (OperationCanceledException)
            {
                AppLog.Info("GitHub did not say in time whether there is a newer SpaceAnalyzer; the kept copy opens.");
                return kept;
            }
        }

        _settingsService.Settings.LastSpaceAnalyzerCheck = DateTime.UtcNow;
        _settingsService.RequestSave();

        if (release is null || release.Version <= kept.Version)
        {
            return kept;
        }

        try
        {
            var install = await InstallSpaceAnalyzerAsync(release);
            AppLog.Info($"SpaceAnalyzer updated from {kept.Version} to {install.Version}.");
            return install;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Warning($"SpaceAnalyzer {release.Version} could not be downloaded; {kept.Version} opens instead.", ex);
            SetStatus(Localization.Strings.Format("SpaceAnalyzer_UpdateFailed", release.Version, kept.Version));
            return kept;
        }
    }

    /// <summary>Downloads and keeps <paramref name="release"/>, with the progress in the status bar.</summary>
    private async Task<SpaceAnalyzerInstall> InstallSpaceAnalyzerAsync(SpaceAnalyzerRelease release)
    {
        // Reports are posted to this thread and can still be queued when the download ends; after that they must
        // not paint a percentage over the status that follows.
        var finished = false;
        var shown = -1;
        var progress = new Progress<double>(fraction =>
        {
            var percent = (int)(fraction * 100);
            if (!finished && percent != shown)
            {
                shown = percent;
                SetStatus(Localization.Strings.Format("SpaceAnalyzer_Downloading", release.Version, percent));
            }
        });

        SetStatus(Localization.Strings.Format("SpaceAnalyzer_Downloading", release.Version, 0));

        try
        {
            return await _spaceAnalyzer.InstallAsync(release, progress);
        }
        finally
        {
            finished = true;
        }
    }

    // ---- remote servers ---------------------------------------------------------------------------------

    /// <summary>
    /// Connects to a saved server and points the active pane at it. From then on that pane behaves like any
    /// other: F5, F6, F7, F8 and rename all work, and copying to the opposite pane transfers the files.
    /// </summary>
    [RelayCommand]
    private Task ConnectSftpAsync() => RunGuardedAsync("SFTP", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var (site, typedPassword) = await _dialogService.ShowSftpConnectAsync();
        if (site is null)
        {
            return;
        }

        // A password typed in the dialog wins; otherwise the keychain, and only then do we ask.
        var secret = typedPassword ?? await SecretForAsync(site);
        SetStatus($"Connecting to {site.DisplayName}...");

        var start = await _connections.ConnectAsync(site, secret);

        await panel.NavigateToCommand.ExecuteAsync(start);
        panel.RequestFocus();
        SetStatus($"Connected to {site.DisplayName}.");
    });

    /// <summary>Closes the connection the active pane is on and brings it back to a local folder.</summary>
    [RelayCommand]
    private Task DisconnectSftpAsync() => RunGuardedAsync("SFTP", async () =>
    {
        if (ActivePanel is not { } panel || SftpAddress.TryParse(panel.CurrentPath) is not { } address)
        {
            SetStatus("This panel is not on a server.");
            return;
        }

        _connections.Disconnect(address.Endpoint);

        // Both panes may have been on that server; whichever was goes home rather than showing a dead listing.
        foreach (var other in new[] { LeftPanel, RightPanel })
        {
            if (SftpAddress.TryParse(other.CurrentPath) is { } on && on.Endpoint == address.Endpoint)
            {
                await other.NavigateToCommand.ExecuteAsync(_fileSystemService.GetDefaultDirectory());
            }
        }

        SetStatus($"Disconnected from {address.Endpoint}.");
    });

    /// <summary>
    /// The password for a site: from the keychain when it is there, otherwise asked for and, with the user's
    /// agreement, remembered. A key with no passphrase needs nothing at all.
    /// </summary>
    private async Task<string?> SecretForAsync(SftpSite site)
    {
        if (await _secrets.GetAsync(site.CredentialKey) is { Length: > 0 } stored)
        {
            return stored;
        }

        if (site.Authentication == SftpAuthentication.PrivateKey)
        {
            // The key is tried unencrypted first; a passphrase is only asked for if that fails.
            return null;
        }

        var typed = await _dialogService.ShowInputDialogAsync(
            "Connect",
            $"Password for {site.DisplayName}:",
            string.Empty,
            value => string.IsNullOrEmpty(value) ? "Enter the password." : null);

        if (typed is null)
        {
            throw new OperationCanceledException();
        }

        if (_secrets.IsAvailable && await _dialogService.ShowConfirmAsync(
                "Connect",
                "Remember this password in the system keychain?",
                "Remember",
                "Just this once"))
        {
            await _secrets.SetAsync(site.CredentialKey, typed);
        }

        return typed;
    }

    // ---- comparing the two panes -------------------------------------------------------------------------

    /// <summary>
    /// Compares the folders the two panes show and selects, on each side, what is missing from the other or
    /// differs from it. Files are compared by size and write time, not by content, so it stays fast on big
    /// folders; use the checksum window when certainty matters.
    /// </summary>
    [RelayCommand]
    private Task CompareDirectoriesAsync() => RunGuardedAsync("Compare folders", async () =>
    {
        if (await CompareAsync() is not { } comparison)
        {
            return;
        }

        LeftPanel.SelectNames(comparison.Items
            .Where(item => item.State is ComparisonState.OnlyLeft or ComparisonState.Different)
            .Select(item => item.Name));

        RightPanel.SelectNames(comparison.Items
            .Where(item => item.State is ComparisonState.OnlyRight or ComparisonState.Different)
            .Select(item => item.Name));

        SetStatus(comparison.OnlyLeft + comparison.OnlyRight + comparison.Different == 0
            ? $"The two folders match ({comparison.Same} items)."
            : $"{comparison.Different} differ, {comparison.OnlyLeft} only on the left, {comparison.OnlyRight} only on the right.");
    });

    /// <summary>
    /// Copies from the active pane to the other one everything that is missing there or differs, leaving the
    /// other pane's extra files alone: this makes the target contain the source, it does not mirror it. The copy
    /// goes through the operations service, so progress, conflicts and errors behave as they do everywhere else.
    /// </summary>
    [RelayCommand]
    private Task SynchronizeDirectoriesAsync() => RunGuardedAsync("Synchronize folders", async () =>
    {
        if (ActivePanel is not { } source || await CompareAsync() is not { } comparison)
        {
            return;
        }

        var fromLeft = ReferenceEquals(source, LeftPanel);
        var wanted = fromLeft ? ComparisonState.OnlyLeft : ComparisonState.OnlyRight;

        var names = comparison.Items
            .Where(item => item.State == wanted || item.State == ComparisonState.Different)
            .Select(item => item.Name)
            .ToList();

        if (names.Count == 0)
        {
            SetStatus("Nothing to copy: the other panel already has everything.");
            return;
        }

        var target = OtherPanel(source);
        var confirmed = await _dialogService.ShowConfirmAsync(
            "Synchronize folders",
            $"Copy {names.Count} item{(names.Count == 1 ? string.Empty : "s")} from\n{source.CurrentPath}\nto\n{target.CurrentPath}?\n\n" +
            "Items only in the destination are left alone.",
            "Copy",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        var sources = names.Select(name => Path.Combine(source.CurrentPath, name)).ToList();
        await _operations.CopyAsync(sources, target.CurrentPath);
    });

    /// <summary>
    /// Makes the other pane an exact copy of the active one: everything missing or different is copied over, and
    /// whatever the destination has that the source does not is deleted. Destructive in a way plain
    /// synchronization is not, so it is confirmed separately and spells out what will be removed.
    /// </summary>
    [RelayCommand]
    private Task MirrorDirectoriesAsync() => RunGuardedAsync("Mirror folders", async () =>
    {
        if (ActivePanel is not { } source || await CompareAsync() is not { } comparison)
        {
            return;
        }

        var fromLeft = ReferenceEquals(source, LeftPanel);
        var missingHere = fromLeft ? ComparisonState.OnlyLeft : ComparisonState.OnlyRight;
        var extraThere = fromLeft ? ComparisonState.OnlyRight : ComparisonState.OnlyLeft;

        var toCopy = comparison.Items
            .Where(item => item.State == missingHere || item.State == ComparisonState.Different)
            .Select(item => item.Name)
            .ToList();

        var toDelete = comparison.Items
            .Where(item => item.State == extraThere)
            .Select(item => item.Name)
            .ToList();

        if (toCopy.Count == 0 && toDelete.Count == 0)
        {
            SetStatus("The two folders already match.");
            return;
        }

        var target = OtherPanel(source);
        var confirmed = await _dialogService.ShowConfirmAsync(
            "Mirror folders",
            $"Make\n{target.CurrentPath}\nan exact copy of\n{source.CurrentPath}?\n\n" +
            $"{toCopy.Count} item{(toCopy.Count == 1 ? string.Empty : "s")} will be copied and " +
            $"{toDelete.Count} item{(toDelete.Count == 1 ? string.Empty : "s")} will be DELETED from the destination.",
            "Mirror",
            "Cancel",
            destructive: true);

        if (!confirmed)
        {
            return;
        }

        // Delete first: the deletion asks for its own confirmation and honours the trash, and doing it before
        // the copy means a cancelled batch leaves the destination with fewer files, never with stale extras.
        if (toDelete.Count > 0)
        {
            var victims = target.Entries
                .Where(entry => toDelete.Contains(entry.Name, PathUtils.PathComparer))
                .ToList();

            if (victims.Count > 0)
            {
                await _operations.DeleteAsync(victims, permanentRequested: false);
            }
        }

        if (toCopy.Count > 0)
        {
            var sources = toCopy.Select(name => Path.Combine(source.CurrentPath, name)).ToList();
            await _operations.CopyAsync(sources, target.CurrentPath);
        }
    });

    /// <summary>Stops a comparison that is taking too long on a large pair of folders.</summary>
    [RelayCommand]
    private void CancelComparison()
    {
        _comparison?.Cancel();
    }

    /// <summary>
    /// Compares the two panes, or explains why it cannot and returns null. Walking two large folders takes time,
    /// so the run is cancellable and only one runs at a time.
    /// </summary>
    private async Task<DirectoryComparison?> CompareAsync()
    {
        if (LeftPanel.IsRootView || RightPanel.IsRootView
            || string.IsNullOrEmpty(LeftPanel.CurrentPath) || string.IsNullOrEmpty(RightPanel.CurrentPath))
        {
            SetStatus("Open a folder in both panels first.");
            return null;
        }

        if (PathUtils.AreSamePath(LeftPanel.CurrentPath, RightPanel.CurrentPath))
        {
            SetStatus("Both panels show the same folder.");
            return null;
        }

        if (IsComparing)
        {
            SetStatus("A comparison is already running.");
            return null;
        }

        using var cancellation = new CancellationTokenSource();
        _comparison = cancellation;
        IsComparing = true;
        SetStatus("Comparing...");

        try
        {
            return await _directoryComparer.CompareAsync(
                LeftPanel.CurrentPath,
                RightPanel.CurrentPath,
                LeftPanel.ShowHiddenFiles || RightPanel.ShowHiddenFiles,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Comparison cancelled.");
            return null;
        }
        finally
        {
            IsComparing = false;
            _comparison = null;
        }
    }

    /// <summary>
    /// Compares the file selected in each pane byte for byte and reports whether they match. Two files of equal
    /// size and date can still differ, which the folder comparison cannot tell; this is the answer for the cases
    /// that need certainty.
    /// </summary>
    [RelayCommand]
    private Task CompareFilesAsync() => RunGuardedAsync("Compare files", async () =>
    {
        if (LeftPanel.SelectedEntry is not { EntryType: FileSystemEntryType.File } left
            || RightPanel.SelectedEntry is not { EntryType: FileSystemEntryType.File } right)
        {
            SetStatus("Select a file in each panel first.");
            return;
        }

        SetStatus($"Comparing '{left.Name}' and '{right.Name}'...");

        var difference = await _fileSystemService.FindFirstDifferenceAsync(left.FullPath, right.FullPath);

        if (difference < 0)
        {
            SetStatus($"'{left.Name}' and '{right.Name}' are identical ({FileSizeFormatter.Format(left.Size)}).");
            return;
        }

        await _dialogService.ShowErrorAsync(
            "Compare files",
            $"'{left.Name}' and '{right.Name}' differ.\n\n" +
            $"First difference at byte {difference:N0}.\n" +
            $"Sizes: {FileSizeFormatter.Format(left.Size)} and {FileSizeFormatter.Format(right.Size)}.");

        SetStatus($"The files differ at byte {difference:N0}.");
    });

    // ---- archives ---------------------------------------------------------------------------------------

    /// <summary>
    /// Packs the selection into a new zip. Like copy and move, it is written to the other pane, which is where a
    /// two-pane file manager puts what it produces; the name is asked for and never overwrites an existing file.
    /// </summary>
    [RelayCommand]
    private Task PackAsync() => RunGuardedAsync("Pack", async () =>
    {
        if (ActivePanel is not { } source)
        {
            return;
        }

        var items = source.GetSelectedItems()
            .Where(item => item.EntryType is FileSystemEntryType.File or FileSystemEntryType.Directory)
            .ToList();

        if (items.Count == 0)
        {
            SetStatus("Select what to pack first.");
            return;
        }

        var destination = DestinationFor(source);
        if (destination is null)
        {
            SetStatus("Open a destination folder in the other panel first.");
            return;
        }

        var suggested = items.Count == 1
            ? Path.GetFileNameWithoutExtension(items[0].Name) + ".zip"
            : Path.GetFileName(source.CurrentPath) + ".zip";

        var name = await _dialogService.ShowInputDialogAsync(
            "Pack",
            $"Name of the archive to create in '{destination}':",
            suggested,
            PathUtils.ValidateFileName);

        if (name is null)
        {
            return;
        }

        var archivePath = Path.Combine(destination, name.Trim());
        if (File.Exists(archivePath) || Directory.Exists(archivePath))
        {
            await _dialogService.ShowErrorAsync("Pack", $"'{name.Trim()}' already exists in the destination folder.");
            return;
        }

        var written = await _archiveService.CreateAsync(items.Select(item => item.FullPath).ToList(), archivePath);

        SetStatus($"Packed {written} file{(written == 1 ? string.Empty : "s")} into '{Path.GetFileName(archivePath)}'.");
        await RefreshAsync();
    });

    /// <summary>
    /// Extracts the selected archives into the other pane, each into a folder of its own named after the archive.
    /// A folder is used rather than the bare destination so an archive full of loose files cannot bury it, and a
    /// free name is picked so nothing already there is touched.
    /// </summary>
    [RelayCommand]
    private Task ExtractAsync() => RunGuardedAsync("Extract", async () =>
    {
        if (ActivePanel is not { } source)
        {
            return;
        }

        var archives = source.GetSelectedItems()
            .Where(item => item.EntryType == FileSystemEntryType.File && _archiveService.IsArchive(item.FullPath))
            .ToList();

        if (archives.Count == 0)
        {
            SetStatus("Select a .zip archive to extract.");
            return;
        }

        var destination = DestinationFor(source);
        if (destination is null)
        {
            SetStatus("Open a destination folder in the other panel first.");
            return;
        }

        var total = 0;
        foreach (var archive in archives)
        {
            var folder = PathUtils.GetUniqueName(destination, Path.GetFileNameWithoutExtension(archive.Name));
            total += await _archiveService.ExtractAsync(archive.FullPath, Path.Combine(destination, folder));
        }

        SetStatus($"Extracted {total} file{(total == 1 ? string.Empty : "s")} into '{destination}'.");
        await RefreshAsync();
    });

    /// <summary>The other pane's folder, or null when it has none (the volume list).</summary>
    private string? DestinationFor(FilePanelViewModel source)
    {
        var other = OtherPanel(source);
        return other.IsRootView || string.IsNullOrEmpty(other.CurrentPath) ? null : other.CurrentPath;
    }

    [RelayCommand]
    private Task SwapPanelsAsync() => RunGuardedAsync("Swap panels", async () =>
    {
        var leftPath = LeftPanel.CurrentPath;
        var rightPath = RightPanel.CurrentPath;

        await Task.WhenAll(
            LeftPanel.NavigateToCommand.ExecuteAsync(rightPath),
            RightPanel.NavigateToCommand.ExecuteAsync(leftPath));
    });

    /// <summary>
    /// Opens a tab. <paramref name="which"/> names the pane ("Left" or "Right"), which is what the "+" at each end
    /// of the tab bar passes; the keyboard shortcut passes nothing and gets the pane that has the focus.
    /// </summary>
    [RelayCommand]
    private Task NewTabAsync(string? which) => RunGuardedAsync("New tab", async () =>
    {
        // The tab belongs to the side that opened it and starts where that side already is.
        var side = which is { Length: > 0 }
            ? ParseSide(which)
            : ActivePanel is { } active ? SideOf(active) : PanelSide.Left;
        var tab = new TabViewModel(side, PathOf(PanelOf(side)));

        AddTab(tab);
        await ActivateAsync(tab);
        SetStatus("New tab opened.");
    });

    /// <summary>
    /// Opens a folder in a new tab owned by <paramref name="panel"/>: that side starts at the folder and the
    /// other one keeps the path it has now. The panel is passed in rather than read from <see cref="ActivePanel"/>
    /// so the tab lands on the side that was actually clicked, whatever happened to hold the focus. Reached from
    /// the middle click on a row and from the context menu; what may be opened is decided here, once.
    /// </summary>
    public Task OpenInNewTabAsync(FilePanelViewModel? panel, FileSystemEntry? entry) => RunGuardedAsync("Open in new tab", async () =>
    {
        if (entry is null || panel is null)
        {
            return;
        }

        if (!ReferenceEquals(panel, LeftPanel) && !ReferenceEquals(panel, RightPanel))
        {
            return;
        }

        if (entry.EntryType is not (FileSystemEntryType.Directory or FileSystemEntryType.Drive or FileSystemEntryType.ParentDirectory)
            || string.IsNullOrEmpty(entry.FullPath))
        {
            SetStatus("Select a folder to open in a new tab.");
            return;
        }

        var tab = new TabViewModel(SideOf(panel), entry.FullPath);

        AddTab(tab);
        await ActivateAsync(tab);
        SetStatus($"Opened '{TabViewModel.TitleFor(entry.FullPath)}' in a new tab.");
    });

    /// <summary>Opens a second tab on the same folder and pane as <paramref name="tab"/>.</summary>
    [RelayCommand]
    private Task DuplicateTabAsync(TabViewModel? tab) => RunGuardedAsync("Duplicate tab", async () =>
    {
        tab ??= CurrentTab;
        if (tab is null)
        {
            return;
        }

        var copy = new TabViewModel(tab.Side, ReferenceEquals(tab, CurrentTabOf(tab.Side)) ? PathOf(PanelOf(tab.Side)) : tab.Path);

        AddTab(copy);
        await ActivateAsync(copy);
        SetStatus("Tab duplicated.");
    });

    /// <summary>Pins or unpins a tab. A pinned tab keeps its folder and cannot be closed.</summary>
    [RelayCommand]
    private void TogglePin(TabViewModel? tab)
    {
        tab ??= CurrentTab;
        if (tab is null)
        {
            return;
        }

        tab.IsPinned = !tab.IsPinned;
        SetStatus(tab.IsPinned ? $"'{tab.Title}' pinned." : $"'{tab.Title}' unpinned.");
    }

    [RelayCommand]
    private Task CloseTabAsync(TabViewModel? tab) => RunGuardedAsync("Close tab", async () =>
    {
        tab ??= CurrentTab;
        if (tab is null)
        {
            return;
        }

        var group = GroupOf(tab.Side);
        var index = group.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        // A pane always keeps one tab: it is what the pane comes back to.
        if (group.Count <= 1)
        {
            SetStatus("The last tab of this pane cannot be closed.");
            return;
        }

        if (tab.IsPinned)
        {
            SetStatus($"'{tab.Title}' is pinned. Unpin it first.");
            return;
        }

        var showing = ReferenceEquals(CurrentTabOf(tab.Side), tab);
        RemoveTab(tab);

        if (showing)
        {
            await ActivateAsync(group[index > 0 ? index - 1 : 0]);
        }
        else if (ReferenceEquals(CurrentTab, tab))
        {
            CurrentTab = CurrentTabOf(tab.Side);
        }

        SetStatus("Tab closed.");
    });

    [RelayCommand]
    private Task CloseCurrentTabAsync() => CloseTabAsync(CurrentTab);

    [RelayCommand]
    private Task NextTabAsync() => CycleTabAsync(1);

    [RelayCommand]
    private Task PreviousTabAsync() => CycleTabAsync(-1);

    [RelayCommand]
    private Task SelectTabAsync(TabViewModel? tab) => RunGuardedAsync("Switch tab", async () =>
    {
        if (tab is not null && Tabs.Contains(tab))
        {
            await ActivateAsync(tab);
        }
    });

    // ---- favorites --------------------------------------------------------------------------------------

    [RelayCommand]
    private Task RemoveFavoriteAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null)
        {
            return;
        }

        await _settingsService.RemoveFavoriteAsync(favorite.Path);
        ReloadFavorites(except: null);
        SetStatus($"Removed '{favorite.Name}' from the favorites.");
    });

    [RelayCommand]
    private Task RenameFavoriteAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null)
        {
            return;
        }

        var name = await _dialogService.ShowInputDialogAsync(
            "Rename Favorite",
            $"New name for '{favorite.Name}':",
            favorite.Name,
            value => string.IsNullOrWhiteSpace(value) ? "The name cannot be empty." : null);

        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, favorite.Name, StringComparison.Ordinal))
        {
            return;
        }

        await _settingsService.RenameFavoriteAsync(favorite.Path, name);
        ReloadFavorites(except: null);
        SetStatus($"Renamed favorite to '{name.Trim()}'.");
    });

    [RelayCommand]
    private Task OpenFavoriteInOtherPanelAsync(FavoriteItem? favorite) => RunGuardedAsync("Favorites", async () =>
    {
        if (favorite is null)
        {
            return;
        }

        var target = ReferenceEquals(ActivePanel, RightPanel) ? LeftPanel : RightPanel;
        await target.NavigateToCommand.ExecuteAsync(favorite.Path);
    });

    [RelayCommand]
    private Task RestoreDefaultFavoritesAsync() => RunGuardedAsync("Favorites", async () =>
    {
        await _settingsService.RestoreDefaultFavoritesAsync();
        ReloadFavorites(except: null);
        SetStatus("Default favorites restored.");
    });

    /// <summary>
    /// Moves a favorite to <paramref name="newIndex"/> in the list (drag reorder), renumbers the order of every
    /// favorite, schedules a settings save and reloads the lists of all panels.
    /// </summary>
    public Task MoveFavoriteAsync(FavoriteItem favorite, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        return RunGuardedAsync("Favorites", () =>
        {
            var ordered = _settingsService.Settings.Favorites.OrderBy(item => item.Order).ToList();
            var oldIndex = ordered.IndexOf(favorite);
            if (oldIndex < 0)
            {
                return Task.CompletedTask;
            }

            ordered.RemoveAt(oldIndex);
            ordered.Insert(Math.Clamp(newIndex, 0, ordered.Count), favorite);
            for (var index = 0; index < ordered.Count; index++)
            {
                ordered[index].Order = index;
            }

            _settingsService.RequestSave();
            ReloadFavorites(except: null);
            return Task.CompletedTask;
        });
    }

    // ---- tools and theme --------------------------------------------------------------------------------

    [RelayCommand]
    private Task ShowAdvancedSearchAsync() => RunGuardedAsync("Search", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var entry = await _dialogService.ShowAdvancedSearchDialogAsync(panel.CurrentPath);
        if (entry is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(entry.FullPath) ?? entry.FullPath;
        await panel.NavigateToCommand.ExecuteAsync(folder);
        SelectByPath(panel, entry.FullPath);
        panel.RequestFocus();
    });

    [RelayCommand]
    private Task ShowMassRenameAsync() => RunGuardedAsync("Mass Rename", async () =>
    {
        if (ActivePanel is not { } panel)
        {
            return;
        }

        var paths = panel.GetSelectedItems().Select(item => item.FullPath).ToList();
        if (paths.Count == 0)
        {
            SetStatus("Select the files to rename first.");
            return;
        }

        if (await _dialogService.ShowMassRenameDialogAsync(paths))
        {
            await panel.RefreshAsync();
            panel.RequestFocus();
        }
    });

    [RelayCommand]
    private Task CalculateHashAsync() => RunGuardedAsync("Checksums", async () =>
    {
        if (ActivePanel?.SelectedEntry is not { EntryType: FileSystemEntryType.File } entry)
        {
            SetStatus("Select a file to calculate its checksums.");
            return;
        }

        await _dialogService.ShowHashDialogAsync(entry.FullPath);
    });

    [RelayCommand]
    private void SetTheme(string? theme)
    {
        var name = ApplyTheme(theme);
        _settingsService.Settings.Theme = name;
        _settingsService.RequestSave();
        SetStatus($"Theme: {name}.");
    }

    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private FilePanelViewModel CreatePanel()
    {
        return new FilePanelViewModel(_fileSystemService, _settingsService, _dialogService, _clipboardService, _operations);
    }

    /// <summary>Adds a tab to the flat list and to the group of its own side; the two are only changed together.</summary>
    private void AddTab(TabViewModel tab)
    {
        Tabs.Add(tab);
        (tab.Side == PanelSide.Left ? LeftTabs : RightTabs).Add(tab);
    }

    private void RemoveTab(TabViewModel tab)
    {
        Tabs.Remove(tab);
        LeftTabs.Remove(tab);
        RightTabs.Remove(tab);
    }

    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        ReloadFavorites(except: sender as FilePanelViewModel);
    }

    /// <summary>Reloads the favorites list (and the star state) of both panels.</summary>
    private void ReloadFavorites(FilePanelViewModel? except)
    {
        {
            foreach (var panel in new[] { LeftPanel, RightPanel })
            {
                if (ReferenceEquals(panel, except))
                {
                    continue;
                }

                panel.LoadFavorites();
                panel.IsFavorite = !string.IsNullOrEmpty(panel.CurrentPath) && _settingsService.IsFavorite(panel.CurrentPath);
            }
        }
    }

    private void OnOperationCompleted(object? sender, FileOperationResult result)
    {
        SetStatus(Describe(result));
    }

    /// <summary>Ctrl+Tab moves through the tabs of the active pane only; the other pane stays put.</summary>
    private Task CycleTabAsync(int step) => RunGuardedAsync("Switch tab", async () =>
    {
        if (ActivePanel is not { } active)
        {
            return;
        }

        var group = GroupOf(SideOf(active));
        if (group.Count < 2)
        {
            return;
        }

        var index = group.IndexOf(CurrentTabOf(SideOf(active)));
        await ActivateAsync(group[((index + step) % group.Count + group.Count) % group.Count]);
    });

    private string ApplyTheme(string? theme)
    {
        _themeService.Apply(theme);
        CurrentTheme = _themeService.CurrentTheme;
        return CurrentTheme;
    }

    private static Task RefreshPanelsAsync(params FilePanelViewModel[] panels)
    {
        return Task.WhenAll(panels.Distinct().Select(panel => panel.RefreshAsync()));
    }

    /// <summary>Selects the entry at <paramref name="index"/>, or the last one when the list got shorter.</summary>
    private static void SelectAt(FilePanelViewModel panel, int index)
    {
        if (index < 0 || panel.FilteredEntries.Count == 0)
        {
            return;
        }

        Select(panel, panel.FilteredEntries[Math.Min(index, panel.FilteredEntries.Count - 1)]);
    }

    /// <summary>Selects and reveals the entry with that path (a renamed, created or found entry may be off-screen).</summary>
    private static void SelectByPath(FilePanelViewModel panel, string path)
    {
        panel.SelectPath(path);
    }

    private static void Select(FilePanelViewModel panel, FileSystemEntry entry)
    {
        panel.SelectedEntry = entry;
        panel.SelectedEntries.Clear();
        panel.SelectedEntries.Add(entry);
    }

    private async Task RunGuardedAsync(string title, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{title} cancelled.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"{title} failed.", ex);
            SetStatus($"{title} failed: {ex.Message}");
            await _dialogService.ShowErrorAsync(title, ex.Message, ex.ToString());
        }
    }

    private void SetStatus(string message)
    {
        StatusMessage = $"{DateTime.Now:HH:mm:ss}  {message}";
    }

    private static string Describe(FileOperationResult result)
    {
        var verb = result.Kind switch
        {
            FileOperationKind.Copy => "Copied",
            FileOperationKind.Move => "Moved",
            FileOperationKind.Delete => "Deleted",
            FileOperationKind.Trash => "Moved to the trash",
            _ => "Processed"
        };

        var details = new List<string>();
        if (result.Skipped > 0)
        {
            details.Add($"{result.Skipped} skipped");
        }

        if (result.Failed > 0)
        {
            details.Add($"{result.Failed} failed");
        }

        var text = $"{verb} {Plural(result.Succeeded, "item")}";
        if (details.Count > 0)
        {
            text += $" ({string.Join(", ", details)})";
        }

        return result.Cancelled ? $"Cancelled. {text}." : $"{text}.";
    }

    private static string Plural(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }

    private static string ReadVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadata = informational.IndexOf('+');
            return metadata > 0 ? informational[..metadata] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // The last look at the session while the panels and the tab collections still hold it: whatever saves
        // after this point (a flush here, the shutdown save of the application lifetime) writes these values.
        try
        {
            CaptureState();
        }
        catch (Exception ex)
        {
            AppLog.Error("The session state could not be captured while closing.", ex);
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _operations.OperationCompleted -= OnOperationCompleted;

        LeftPanel.FavoritesChanged -= OnFavoritesChanged;
        RightPanel.FavoritesChanged -= OnFavoritesChanged;
        LeftPanel.PropertyChanged -= OnLeftPanelPropertyChanged;
        RightPanel.PropertyChanged -= OnRightPanelPropertyChanged;
        LeftPanel.Dispose();
        RightPanel.Dispose();

        Tabs.Clear();
        LeftTabs.Clear();
        RightTabs.Clear();

        // Best effort; ShutdownAsync is the awaited path.
        _ = FlushSettingsQuietlyAsync();
    }

    private async Task FlushSettingsQuietlyAsync()
    {
        try
        {
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("The settings could not be flushed.", ex);
        }
    }
}
