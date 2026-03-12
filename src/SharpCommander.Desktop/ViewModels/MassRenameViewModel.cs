using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SharpCommander.Desktop.ViewModels;

public partial class MassRenameViewModel : ObservableObject
{
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

    [ObservableProperty]
    private decimal _counterStart = 1;

    [ObservableProperty]
    private decimal _counterStep = 1;

    [ObservableProperty]
    private decimal _counterPadding = 1;

    [ObservableProperty]
    private int _caseOption = 0; // 0=None, 1=Lower, 2=Upper, 3=Title

    [ObservableProperty]
    private ObservableCollection<RenameItem> _items = [];

    [ObservableProperty]
    private string _statusText = "Ready";

    public MassRenameViewModel(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            Items.Add(new RenameItem(path));
        }

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(StatusText) && e.PropertyName != nameof(Items))
            {
                Preview();
            }
        };

        Preview(); // Initial preview
    }

    [RelayCommand]
    private void Preview()
    {
        int counter = (int)CounterStart;

        foreach (var item in Items)
        {
            var originalName = Path.GetFileNameWithoutExtension(item.OldName);
            var originalExt = Path.GetExtension(item.OldName);

            // Apply Masks
            var newNameBase = FileNameMask?.Replace("[N]", originalName) ?? string.Empty;
            var newExtBase = ExtensionMask?.Replace("[E]", originalExt) ?? string.Empty;

            // Apply Counter
            var counterStr = counter.ToString($"D{(int)CounterPadding}");
            newNameBase = newNameBase.Replace("[C]", counterStr);
            newExtBase = newExtBase.Replace("[C]", counterStr);
            counter += (int)CounterStep;

            var finalName = newNameBase + newExtBase;

            // Apply Search & Replace
            if (!string.IsNullOrEmpty(SearchPattern))
            {
                try
                {
                    if (UseRegex)
                    {
                        finalName = Regex.Replace(finalName, SearchPattern, ReplacePattern ?? string.Empty);
                    }
                    else
                    {
                        finalName = finalName.Replace(SearchPattern, ReplacePattern ?? string.Empty);
                    }
                }
                catch
                {
                    // Invalid regex, skip replacement
                }
            }

            // Apply Casing
            if (CaseOption == 1) // Lowercase
            {
                finalName = finalName.ToLower();
            }
            else if (CaseOption == 2) // Uppercase
            {
                finalName = finalName.ToUpper();
            }
            else if (CaseOption == 3) // Title Case
            {
                var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
                finalName = textInfo.ToTitleCase(finalName.ToLower());
            }

            // Fallback if empty
            if (string.IsNullOrWhiteSpace(finalName))
            {
                finalName = item.OldName;
            }

            item.NewName = finalName;
        }
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

    [RelayCommand]
    private void Apply()
    {
        int count = 0;
        foreach (var item in Items)
        {
            if (item.OldName == item.NewName) continue;

            try
            {
                var dir = Path.GetDirectoryName(item.FullPath);
                if (dir == null) continue;

                // Reject names that contain path separators to prevent traversal
                if (item.NewName.Contains(Path.DirectorySeparatorChar) ||
                    item.NewName.Contains(Path.AltDirectorySeparatorChar))
                {
                    continue;
                }

                var newPath = Path.Combine(dir, item.NewName);
                
                if (!File.Exists(newPath) && !Directory.Exists(newPath))
                {
                    if (File.Exists(item.FullPath))
                    {
                        File.Move(item.FullPath, newPath);
                    }
                    else if (Directory.Exists(item.FullPath))
                    {
                        Directory.Move(item.FullPath, newPath);
                    }
                    count++;
                }
            }
            catch { /* Ignore errors for individual files */ }
        }
        StatusText = $"Renamed {count} files. Close window to see changes.";
    }
}

public partial class RenameItem : ObservableObject
{
    public string FullPath { get; }
    public string OldName { get; }

    [ObservableProperty]
    private string _newName = string.Empty;

    public RenameItem(string fullPath)
    {
        FullPath = fullPath;
        OldName = Path.GetFileName(fullPath);
        NewName = OldName;
    }
}
