namespace SharpCommander.Core.Models;

/// <summary>
/// The kind of batch run through <see cref="Interfaces.IFileOperationsService"/>.
/// </summary>
public enum FileOperationKind
{
    Copy,
    Move,

    /// <summary>Permanent deletion.</summary>
    Delete,

    /// <summary>Deletion through the operating system trash.</summary>
    Trash
}

/// <summary>
/// Outcome of a batch operation. The counts refer to the top-level items the user selected; files skipped
/// inside a merged folder are added to <see cref="Skipped"/> as well. Every failed item is listed in
/// <see cref="Errors"/> with a user-facing message; the service has already shown them to the user.
/// </summary>
public sealed record FileOperationResult
{
    public required FileOperationKind Kind { get; init; }

    /// <summary>Items fully processed (a merged folder counts as one even when some of its files were skipped).</summary>
    public int Succeeded { get; init; }

    /// <summary>Top-level items skipped, plus files skipped inside merged folders.</summary>
    public int Skipped { get; init; }

    /// <summary>Items that failed; one entry per item in <see cref="Errors"/>.</summary>
    public int Failed { get; init; }

    public IReadOnlyList<FileOperationError> Errors { get; init; } = [];

    /// <summary>
    /// True when the user cancelled: through the Cancel button, a conflict answered with Cancel, or by declining
    /// the confirmation. Items processed before the cancellation are still counted.
    /// </summary>
    public bool Cancelled { get; init; }

    /// <summary>
    /// True when the batch did not start because another operation was running or the request was invalid;
    /// an error was already shown to the user.
    /// </summary>
    public bool Rejected { get; init; }

    /// <summary>True when at least one item was touched, so callers should refresh their listings.</summary>
    public bool Started { get; init; }

    public int Total => Succeeded + Skipped + Failed;

    public bool HasErrors => Errors.Count > 0;

    /// <summary>A result for a batch that never touched the disk.</summary>
    public static FileOperationResult NotStarted(FileOperationKind kind, bool rejected) => new()
    {
        Kind = kind,
        Rejected = rejected,
        Cancelled = !rejected
    };
}
