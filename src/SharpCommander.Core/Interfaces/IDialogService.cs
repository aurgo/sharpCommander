using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Every dialog the view models can open. Implementations own the windows; view models never touch controls.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows the hash calculation window for a file.</summary>
    Task ShowHashDialogAsync(string filePath);

    /// <summary>Shows the advanced search window. Returns the entry the user activated, or null.</summary>
    Task<FileSystemEntry?> ShowAdvancedSearchDialogAsync(string initialPath);

    /// <summary>Shows the mass rename window. Returns true when at least one entry was renamed.</summary>
    Task<bool> ShowMassRenameDialogAsync(IReadOnlyList<string> paths);

    /// <summary>Shows the properties window for an entry.</summary>
    Task ShowPropertiesDialogAsync(FileSystemEntry entry);

    /// <summary>Shows the internal read-only viewer (F3) for a file.</summary>
    Task ShowViewerAsync(string filePath);

    /// <summary>
    /// Asks for a single line of text. <paramref name="validate"/> returns an error text shown inline (with OK
    /// disabled) or null when the value is acceptable. Returns null when cancelled.
    /// </summary>
    Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue = "", Func<string, string?>? validate = null);

    /// <summary>Asks a yes/no question. <paramref name="destructive"/> styles the confirm button as dangerous.</summary>
    /// <param name="defaultIsCancel">
    /// Arms the cancel button instead of the confirm one, for a prompt that follows another confirmation and
    /// could otherwise be answered by a still-repeating Enter.
    /// </param>
    Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false, bool defaultIsCancel = false);

    /// <summary>
    /// Confirms deleting <paramref name="items"/>. Trash is the default choice when <paramref name="trashAvailable"/>;
    /// <paramref name="permanentRequested"/> (Shift+Delete) asks for permanent deletion instead.
    /// </summary>
    Task<DeleteChoice> ShowDeleteConfirmAsync(IReadOnlyList<FileSystemEntry> items, bool trashAvailable, bool permanentRequested);

    /// <summary>
    /// Resolves a name collision. <paramref name="remainingConflicts"/> greater than zero enables "apply to all".
    /// </summary>
    Task<ConflictResolution> ShowConflictAsync(FileConflict conflict, int remainingConflicts);

    /// <summary>Shows an error with an optional expandable details section.</summary>
    Task ShowErrorAsync(string title, string message, string? details = null);

    /// <summary>Shows the items that failed in a batch operation, one line per item.</summary>
    Task ShowOperationErrorsAsync(string title, IReadOnlyList<FileOperationError> errors);
}
