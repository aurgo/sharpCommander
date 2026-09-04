using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>
/// Scriptable dialog service: tests decide what the "user" answers. Records every call.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>Every dialog shown, as "kind:detail" strings in call order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Answer for input dialogs; null cancels.</summary>
    public string? InputAnswer { get; set; }

    /// <summary>Answer for yes/no confirmations.</summary>
    public bool ConfirmAnswer { get; set; } = true;

    /// <summary>Answer for delete confirmations.</summary>
    public DeleteChoice DeleteAnswer { get; set; } = DeleteChoice.Permanent;

    /// <summary>Answer for name conflicts.</summary>
    public ConflictResolution ConflictAnswer { get; set; } = new(ConflictAction.Skip);

    /// <summary>When set, decides each conflict instead of <see cref="ConflictAnswer"/> (may await).</summary>
    public Func<FileConflict, int, Task<ConflictResolution>>? ConflictHandler { get; set; }

    /// <summary>Entry "activated" in the advanced search window; null when the user just closes it.</summary>
    public FileSystemEntry? SearchAnswer { get; set; }

    /// <summary>Result of the mass rename window.</summary>
    public bool MassRenameAnswer { get; set; }

    /// <summary>When set, runs with the paths before <see cref="MassRenameAnswer"/> is returned (the renames the window applied).</summary>
    public Action<IReadOnlyList<string>>? MassRenameHandler { get; set; }

    /// <summary>Messages passed to <see cref="ShowErrorAsync"/>.</summary>
    public List<string> ErrorMessages { get; } = [];

    /// <summary>Errors passed to <see cref="ShowOperationErrorsAsync"/>.</summary>
    public List<FileOperationError> ReportedErrors { get; } = [];

    public Task ShowHashDialogAsync(string filePath)
    {
        Calls.Add("hash:" + filePath);
        return Task.CompletedTask;
    }

    public Task<FileSystemEntry?> ShowAdvancedSearchDialogAsync(string initialPath)
    {
        Calls.Add("search:" + initialPath);
        return Task.FromResult(SearchAnswer);
    }

    public Task<bool> ShowMassRenameDialogAsync(IReadOnlyList<string> paths)
    {
        Calls.Add("massrename:" + paths.Count);
        MassRenameHandler?.Invoke(paths);
        return Task.FromResult(MassRenameAnswer);
    }

    public Task ShowPropertiesDialogAsync(FileSystemEntry entry)
    {
        Calls.Add("properties:" + entry.FullPath);
        return Task.CompletedTask;
    }

    public Task ShowViewerAsync(string filePath)
    {
        Calls.Add("viewer:" + filePath);
        return Task.CompletedTask;
    }

    public Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue = "", Func<string, string?>? validate = null)
    {
        Calls.Add("input:" + title);

        // Like the real dialog, an answer the validator rejects cannot be submitted.
        if (InputAnswer is not null && validate?.Invoke(InputAnswer) is not null)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(InputAnswer);
    }

    public Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool destructive = false)
    {
        Calls.Add("confirm:" + title);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task<DeleteChoice> ShowDeleteConfirmAsync(IReadOnlyList<FileSystemEntry> items, bool trashAvailable, bool permanentRequested)
    {
        Calls.Add($"delete:{items.Count}:{(trashAvailable ? "trash" : "notrash")}:{(permanentRequested ? "permanent" : "default")}");
        return Task.FromResult(DeleteAnswer);
    }

    public Task<ConflictResolution> ShowConflictAsync(FileConflict conflict, int remainingConflicts)
    {
        Calls.Add("conflict:" + conflict.DestinationPath);
        return ConflictHandler is not null ? ConflictHandler(conflict, remainingConflicts) : Task.FromResult(ConflictAnswer);
    }

    public Task ShowErrorAsync(string title, string message, string? details = null)
    {
        Calls.Add("error:" + title);
        ErrorMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task ShowOperationErrorsAsync(string title, IReadOnlyList<FileOperationError> errors)
    {
        Calls.Add("operationerrors:" + title);
        ReportedErrors.AddRange(errors);
        return Task.CompletedTask;
    }
}
