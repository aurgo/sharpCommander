using System.ComponentModel;
using System.Windows.Input;
using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Single entry point for every user-triggered file operation (function keys, clipboard, context menu, drag and
/// drop). Every batch shares one policy: sources are validated up front (nothing is touched when a source
/// would be moved into itself or onto its own location), name collisions ask the user with "apply to all"
/// remembered for the rest of the batch, deletion is confirmed and goes to the trash by default, per-item errors
/// are collected and shown once at the end, and the batch can be cancelled. Only one batch runs at a time; the
/// state properties describe it for a status bar and change on the UI thread.
/// </summary>
public interface IFileOperationsService : INotifyPropertyChanged
{
    /// <summary>True while a batch is running.</summary>
    bool IsRunning { get; }

    /// <summary>Human readable name of the running batch ("Copying", "Moving to the trash"), empty when idle.</summary>
    string OperationName { get; }

    /// <summary>Full path of the file being processed, empty when idle.</summary>
    string CurrentFile { get; }

    /// <summary>Completion of the whole batch, 0 to 100: by bytes when the total is known, else by items.</summary>
    double Percent { get; }

    long ProcessedBytes { get; }

    long TotalBytes { get; }

    int ProcessedItems { get; }

    int TotalItems { get; }

    /// <summary>Cancels the running batch; enabled only while <see cref="IsRunning"/>.</summary>
    ICommand CancelCommand { get; }

    /// <summary>Raised after every batch that started (also when cancelled), with its result.</summary>
    event EventHandler<FileOperationResult>? OperationCompleted;

    /// <summary>Cancels the running batch, if any.</summary>
    void Cancel();

    /// <summary>
    /// Completes once the running batch has finished (including the engine's cleanup of partial files after a
    /// cancellation); already completed when nothing is running. Await it after <see cref="Cancel"/> before the
    /// process exits.
    /// </summary>
    Task WhenIdleAsync();

    /// <summary>Copies each source (file or directory) into <paramref name="destinationDirectory"/>.</summary>
    Task<FileOperationResult> CopyAsync(IReadOnlyList<string> sources, string destinationDirectory, CancellationToken cancellationToken = default);

    /// <summary>Moves each source into <paramref name="destinationDirectory"/>; directories merge into existing ones.</summary>
    Task<FileOperationResult> MoveAsync(IReadOnlyList<string> sources, string destinationDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for confirmation (trash by default when available, permanent when <paramref name="permanentRequested"/>)
    /// and deletes the items. When the trash refuses an item the user is offered to delete it permanently.
    /// </summary>
    Task<FileOperationResult> DeleteAsync(IReadOnlyList<FileSystemEntry> items, bool permanentRequested, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for a new name (validated inline, existing names refused unless only the case changes) and renames the
    /// entry. Returns the new full path, or null when cancelled or failed (the error was shown).
    /// </summary>
    Task<string?> RenameAsync(FileSystemEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for a folder name (validated inline, existing names refused) and creates it inside
    /// <paramref name="parentDirectory"/>. Returns the new full path, or null when cancelled or failed.
    /// </summary>
    Task<string?> CreateDirectoryAsync(string parentDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the entry with its default application, asking first when the file is a program or a script.
    /// Returns true when it was opened; errors are shown to the user.
    /// </summary>
    Task<bool> OpenAsync(FileSystemEntry entry, CancellationToken cancellationToken = default);
}
