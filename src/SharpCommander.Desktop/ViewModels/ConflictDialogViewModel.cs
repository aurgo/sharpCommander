using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// State of the conflict dialog: describes both sides of a name collision, validates the rename box and
/// produces the <see cref="ConflictResolution"/>.
/// </summary>
public sealed partial class ConflictDialogViewModel : ObservableObject
{
    private readonly Func<string, bool> _exists;
    private readonly bool _destinationIsDirectory;

    /// <summary>Gets the conflict being resolved.</summary>
    public FileConflict Conflict { get; }

    /// <summary>Name of the colliding item.</summary>
    public string ItemName { get; }

    /// <summary>Folder that already contains an item with that name.</summary>
    public string DestinationDirectory { get; }

    public string Message => $"An item named '{ItemName}' already exists in '{DestinationDirectory}'.";

    public string SourceKind => Conflict.IsDirectory ? "Folder" : "File";
    public string DestinationKind => _destinationIsDirectory ? "Folder" : "File";
    public string SourcePath => Conflict.SourcePath;
    public string DestinationPath => Conflict.DestinationPath;
    public string SourceSizeText => Conflict.IsDirectory ? "-" : FormatSize(Conflict.SourceSize);
    public string DestinationSizeText => _destinationIsDirectory ? "-" : FormatSize(Conflict.DestinationSize);
    public string SourceModifiedText => FormatDate(Conflict.SourceModified);
    public string DestinationModifiedText => FormatDate(Conflict.DestinationModified);

    /// <summary>True when the newer side is the source, to hint which one the user probably wants.</summary>
    public bool SourceIsNewer => Conflict.SourceModified > Conflict.DestinationModified;
    public bool DestinationIsNewer => Conflict.DestinationModified > Conflict.SourceModified;

    /// <summary>Overwriting is refused when a file would replace a folder or a folder a file.</summary>
    public bool CanOverwrite => Conflict.IsDirectory == _destinationIsDirectory;

    public string OverwriteHint => CanOverwrite ? string.Empty : "A folder cannot replace a file, nor a file a folder.";
    public bool HasOverwriteHint => !CanOverwrite;

    /// <summary>Apply-to-all is offered only when more conflicts may follow.</summary>
    public bool CanApplyToAll { get; }

    public string ApplyToAllText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenameHint))]
    private bool _applyToAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenameError))]
    [NotifyPropertyChangedFor(nameof(HasRenameError))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _newName = string.Empty;

    /// <summary>Validation message for the rename box, or null when the name can be used.</summary>
    public string? RenameError => ValidateNewName();

    public bool HasRenameError => RenameError is not null;

    public string RenameHint => ApplyToAll ? "With apply to all, unique names are generated automatically." : string.Empty;

    /// <summary>The chosen resolution; null until a button was pressed.</summary>
    public ConflictResolution? Result { get; private set; }

    /// <summary>Raised once <see cref="Result"/> is set and the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <param name="conflict">The collision reported by the file system service.</param>
    /// <param name="remainingConflicts">How many more conflicts the operation may raise.</param>
    /// <param name="destinationIsDirectory">Whether the existing destination entry is a folder.</param>
    /// <param name="exists">Tells whether a path exists, used to validate the rename box.</param>
    /// <param name="suggestedName">Initial content of the rename box (a unique name).</param>
    public ConflictDialogViewModel(FileConflict conflict, int remainingConflicts, bool destinationIsDirectory, Func<string, bool> exists, string suggestedName)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(exists);

        Conflict = conflict;
        _destinationIsDirectory = destinationIsDirectory;
        _exists = exists;
        ItemName = Path.GetFileName(conflict.DestinationPath);
        DestinationDirectory = Path.GetDirectoryName(conflict.DestinationPath) ?? conflict.DestinationPath;
        CanApplyToAll = remainingConflicts > 0;
        ApplyToAllText = remainingConflicts == 1
            ? "Apply to the remaining conflict"
            : $"Apply to all remaining conflicts ({remainingConflicts})";
        NewName = suggestedName;
    }

    [RelayCommand(CanExecute = nameof(CanOverwrite))]
    private void Overwrite()
    {
        Finish(new ConflictResolution(ConflictAction.Overwrite, ApplyToAll));
    }

    [RelayCommand]
    private void Skip()
    {
        Finish(new ConflictResolution(ConflictAction.Skip, ApplyToAll));
    }

    private bool CanRename() => !HasRenameError;

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        Finish(new ConflictResolution(ConflictAction.Rename, ApplyToAll, NewName));
    }

    [RelayCommand]
    private void Cancel()
    {
        Finish(new ConflictResolution(ConflictAction.Cancel));
    }

    private void Finish(ConflictResolution resolution)
    {
        Result = resolution;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string? ValidateNewName()
    {
        var error = PathUtils.ValidateFileName(NewName);
        if (error is not null)
        {
            return error;
        }

        if (string.Equals(NewName, ItemName, StringComparison.Ordinal))
        {
            return "Choose a name different from the existing one.";
        }

        return _exists(Path.Combine(DestinationDirectory, NewName)) ? "An item with this name already exists." : null;
    }

    private static string FormatSize(long bytes)
    {
        return $"{FileSizeFormatter.Format(bytes)} ({bytes:N0} bytes)";
    }

    private static string FormatDate(DateTime value)
    {
        return value == default ? "-" : value.ToString("g", CultureInfo.CurrentCulture);
    }
}
