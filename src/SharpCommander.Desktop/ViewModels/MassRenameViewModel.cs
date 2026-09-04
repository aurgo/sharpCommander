using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Outcome of the preview validation for one <see cref="RenameItem"/>.
/// </summary>
public enum RenameStatus
{
    /// <summary>The new name equals the current one; nothing to do.</summary>
    Unchanged,

    /// <summary>The item can be renamed.</summary>
    Ok,

    /// <summary>The new name is not a valid file name on this platform.</summary>
    InvalidName,

    /// <summary>Another item of the batch produces the same name in the same folder.</summary>
    DuplicateInBatch,

    /// <summary>An entry that is not part of the batch already has the new name.</summary>
    TargetExists,

    /// <summary>The last apply failed for this item (see its status text).</summary>
    Failed
}

/// <summary>
/// Mass rename with masks, counter, search and replace and case conversion. The preview validates every
/// new name (invalid, duplicate within the batch, existing target); apply refuses to run while conflicts
/// remain and renames in two phases so chains and cycles (a to b, b to a) work.
/// </summary>
public sealed partial class MassRenameViewModel : ObservableObject
{
    private static readonly HashSet<string> PreviewInputs =
    [
        nameof(SearchPattern), nameof(ReplacePattern), nameof(FileNameMask), nameof(ExtensionMask),
        nameof(UseRegex), nameof(CounterStart), nameof(CounterStep), nameof(CounterPadding), nameof(CaseOption)
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly IFileSystemService _fileSystemService;

    [ObservableProperty]
    private string _searchPattern = string.Empty;

    [ObservableProperty]
    private string _replacePattern = string.Empty;

    [ObservableProperty]
    private string _fileNameMask = "[N]";

    [ObservableProperty]
    private string _extensionMask = "[E]";

    [ObservableProperty]
    private bool _useRegex;

    /// <summary>Counter start; null (an empty NumericUpDown) counts as 1.</summary>
    [ObservableProperty]
    private decimal? _counterStart = 1;

    [ObservableProperty]
    private decimal? _counterStep = 1;

    [ObservableProperty]
    private decimal? _counterPadding = 1;

    /// <summary>0 = no change, 1 = lower case, 2 = upper case, 3 = title case.</summary>
    [ObservableProperty]
    private int _caseOption;

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>"N ok, M unchanged, K conflicts".</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>One line per item that failed in the last apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailures))]
    private string _failureText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _canApply;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    /// <summary>True once at least one entry was renamed; the caller refreshes its panel when set.</summary>
    [ObservableProperty]
    private bool _hasRenamed;

    public ObservableCollection<RenameItem> Items { get; } = [];

    public bool HasFailures => FailureText.Length > 0;

    public MassRenameViewModel(IFileSystemService fileSystemService, IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(filePaths);

        _fileSystemService = fileSystemService;
        foreach (var path in filePaths)
        {
            Items.Add(new RenameItem(path));
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && PreviewInputs.Contains(e.PropertyName))
            {
                Preview();
            }
        };

        Preview();
    }

    /// <summary>Recomputes every new name from the current settings and validates the batch.</summary>
    [RelayCommand]
    public void Preview()
    {
        ComputeNewNames();
        RefreshStatuses(keepFailures: false);
    }

    /// <summary>
    /// Validates the current new names without recomputing them: invalid names, duplicates inside the batch
    /// and targets that already exist and are not part of the batch. Updates the summary and
    /// <see cref="CanApply"/>.
    /// </summary>
    public void RefreshStatuses()
    {
        RefreshStatuses(keepFailures: false);
    }

    [RelayCommand]
    private void InsertMask(string mask)
    {
        FileNameMask += mask;
    }

    [RelayCommand]
    private void InsertMaskExt(string mask)
    {
        ExtensionMask += mask;
    }

    private bool CanExecuteApply() => CanApply && !IsApplying;

    /// <summary>
    /// Renames every item marked <see cref="RenameStatus.Ok"/>. Items whose current name is the target of
    /// another item are first moved to a temporary name so chains and swaps cannot collide. Errors are
    /// collected per item and never abort the batch.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteApply))]
    private async Task ApplyAsync()
    {
        RefreshStatuses(keepFailures: false);
        if (!CanApply)
        {
            return;
        }

        IsApplying = true;
        FailureText = string.Empty;
        StatusText = "Renaming...";

        var pending = Items.Where(item => item.Status == RenameStatus.Ok).ToList();
        var failures = new List<string>();
        var renamed = 0;

        try
        {
            var staged = await StageOccupiedTargetsAsync(pending, failures);

            foreach (var item in pending)
            {
                if (item.Status == RenameStatus.Failed)
                {
                    continue;
                }

                var currentPath = staged.TryGetValue(item, out var temporaryPath) ? temporaryPath : item.FullPath;
                if (await TryRenameAsync(item, currentPath, item.NewName, failures))
                {
                    item.MarkRenamed(item.TargetPath);
                    renamed++;
                }
                else if (temporaryPath is not null)
                {
                    await TryRestoreAsync(item, temporaryPath, failures);
                }
            }
        }
        finally
        {
            IsApplying = false;
        }

        if (renamed > 0)
        {
            HasRenamed = true;
        }

        FailureText = string.Join(Environment.NewLine, failures);
        StatusText = failures.Count == 0
            ? $"Renamed {Plural(renamed, "item", "items")}."
            : $"Renamed {Plural(renamed, "item", "items")}, {failures.Count} failed.";

        RefreshStatuses(keepFailures: true);
    }

    /// <summary>
    /// Phase one: every item whose current path is the target of some item in the batch is renamed to a
    /// temporary unique name, which frees all targets for phase two regardless of order.
    /// </summary>
    private async Task<Dictionary<RenameItem, string>> StageOccupiedTargetsAsync(List<RenameItem> pending, List<string> failures)
    {
        var targets = new HashSet<string>(pending.Select(item => item.TargetPath), PathUtils.PathComparer);
        var staged = new Dictionary<RenameItem, string>();

        foreach (var item in pending)
        {
            if (!targets.Contains(item.FullPath))
            {
                continue;
            }

            var temporaryName = PathUtils.GetUniqueName(item.Directory, $"{item.OldName}.{Guid.NewGuid():N}.renaming");
            if (await TryRenameAsync(item, item.FullPath, temporaryName, failures))
            {
                staged[item] = Path.Combine(item.Directory, temporaryName);
            }
        }

        return staged;
    }

    private async Task<bool> TryRenameAsync(RenameItem item, string currentPath, string newName, List<string> failures)
    {
        try
        {
            await _fileSystemService.RenameAsync(currentPath, newName);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"{item.OldName}: {ex.Message}");
            item.SetStatus(RenameStatus.Failed, $"Failed: {ex.Message}");
            return false;
        }
    }

    private async Task TryRestoreAsync(RenameItem item, string temporaryPath, List<string> failures)
    {
        try
        {
            await _fileSystemService.RenameAsync(temporaryPath, item.OldName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"{item.OldName}: left as '{Path.GetFileName(temporaryPath)}' ({ex.Message})");
            item.MarkRenamed(temporaryPath);
        }
    }

    private void ComputeNewNames()
    {
        var counter = (int)(CounterStart ?? 1);
        var step = (int)(CounterStep ?? 1);
        var padding = Math.Clamp((int)(CounterPadding ?? 1), 1, 10);
        var replacer = CreateReplacer();

        foreach (var item in Items)
        {
            var name = ApplyMasks(item.OldName, counter, padding);
            counter += step;

            name = replacer(name);
            name = ApplyCase(name);

            item.NewName = string.IsNullOrWhiteSpace(name) ? item.OldName : name;
        }
    }

    private string ApplyMasks(string oldName, int counter, int padding)
    {
        var baseName = Path.GetFileNameWithoutExtension(oldName);
        var extension = Path.GetExtension(oldName);
        var counterText = counter.ToString($"D{padding}", CultureInfo.InvariantCulture);

        var newBase = (FileNameMask ?? string.Empty).Replace("[N]", baseName).Replace("[C]", counterText);
        var newExtension = (ExtensionMask ?? string.Empty).Replace("[E]", extension).Replace("[C]", counterText);
        return newBase + newExtension;
    }

    /// <summary>Builds the search and replace step once; an invalid regex is reported and skipped.</summary>
    private Func<string, string> CreateReplacer()
    {
        var search = SearchPattern ?? string.Empty;
        var replacement = ReplacePattern ?? string.Empty;
        if (search.Length == 0)
        {
            StatusText = "Ready";
            return static name => name;
        }

        if (!UseRegex)
        {
            StatusText = "Ready";
            return name => name.Replace(search, replacement, StringComparison.Ordinal);
        }

        try
        {
            var regex = new Regex(search, RegexOptions.None, RegexTimeout);
            StatusText = "Ready";
            return name =>
            {
                try
                {
                    return regex.Replace(name, replacement);
                }
                catch (RegexMatchTimeoutException)
                {
                    return name;
                }
            };
        }
        catch (ArgumentException ex)
        {
            StatusText = $"Invalid regular expression: {ex.Message}";
            return static name => name;
        }
    }

    private string ApplyCase(string name)
    {
        var culture = CultureInfo.CurrentCulture;
        return CaseOption switch
        {
            1 => name.ToLower(culture),
            2 => name.ToUpper(culture),
            3 => culture.TextInfo.ToTitleCase(name.ToLower(culture)),
            _ => name
        };
    }

    private void RefreshStatuses(bool keepFailures)
    {
        var comparer = PathUtils.PathComparer;
        var targetCounts = new Dictionary<string, int>(comparer);
        var currentPaths = new HashSet<string>(comparer);

        foreach (var item in Items)
        {
            currentPaths.Add(item.FullPath);
            var target = item.TargetPath;
            targetCounts[target] = targetCounts.GetValueOrDefault(target) + 1;
        }

        var ok = 0;
        var unchanged = 0;
        var conflicts = 0;

        foreach (var item in Items)
        {
            if (keepFailures && item.Status == RenameStatus.Failed)
            {
                conflicts++;
                continue;
            }

            if (string.Equals(item.NewName, item.OldName, StringComparison.Ordinal))
            {
                item.SetStatus(RenameStatus.Unchanged, "Unchanged");
                unchanged++;
                continue;
            }

            var error = PathUtils.ValidateFileName(item.NewName);
            if (error is not null)
            {
                item.SetStatus(RenameStatus.InvalidName, error);
                conflicts++;
                continue;
            }

            if (targetCounts[item.TargetPath] > 1)
            {
                item.SetStatus(RenameStatus.DuplicateInBatch, "Duplicate name within this batch");
                conflicts++;
                continue;
            }

            // A case-only rename targets the entry itself, so an existing target is that same entry. Testing
            // Exists would refuse it on a case-insensitive volume mounted on Linux, exactly as it did in
            // FileSystemService.Rename.
            var caseOnlyRename = string.Equals(item.OldName, item.NewName, StringComparison.OrdinalIgnoreCase);

            if (!caseOnlyRename && !currentPaths.Contains(item.TargetPath) && _fileSystemService.Exists(item.TargetPath))
            {
                item.SetStatus(RenameStatus.TargetExists, "An item with this name already exists");
                conflicts++;
                continue;
            }

            item.SetStatus(RenameStatus.Ok, "Ok");
            ok++;
        }

        SummaryText = $"{ok} ok, {unchanged} unchanged, {conflicts} conflicts";
        CanApply = ok > 0 && conflicts == 0;
    }

    private static string Plural(int count, string singular, string plural)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }
}

/// <summary>
/// One row of the mass rename preview. <see cref="FullPath"/> and <see cref="OldName"/> follow the entry
/// after a successful rename so the preview can be applied again.
/// </summary>
public sealed partial class RenameItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Directory))]
    private string _fullPath;

    [ObservableProperty]
    private string _oldName;

    [ObservableProperty]
    private string _newName;

    [ObservableProperty]
    private RenameStatus _status;

    /// <summary>Short explanation of <see cref="Status"/> for the preview column.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    public RenameItem(string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        _fullPath = fullPath;
        _oldName = Path.GetFileName(fullPath);
        _newName = _oldName;
    }

    /// <summary>Folder containing the entry.</summary>
    public string Directory => Path.GetDirectoryName(FullPath) ?? string.Empty;

    /// <summary>Full path the entry would have after renaming.</summary>
    public string TargetPath => Path.Combine(Directory, NewName);

    internal void SetStatus(RenameStatus status, string text)
    {
        Status = status;
        StatusText = text;
    }

    internal void MarkRenamed(string newFullPath)
    {
        FullPath = newFullPath;
        OldName = Path.GetFileName(newFullPath);
    }
}
