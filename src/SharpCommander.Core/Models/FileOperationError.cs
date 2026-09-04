namespace SharpCommander.Core.Models;

/// <summary>
/// One failed item of a batch operation, for reporting to the user.
/// </summary>
/// <param name="Path">Full path of the item that failed.</param>
/// <param name="Message">User-facing description of the failure.</param>
public sealed record FileOperationError(string Path, string Message);

/// <summary>
/// Categories of failures detected by the file system service before or while operating.
/// </summary>
public enum FileOperationErrorKind
{
    /// <summary>Source and destination are the same location; nothing was touched.</summary>
    SameLocation,

    /// <summary>The destination lies inside the source directory; nothing was touched.</summary>
    DestinationInsideSource,

    /// <summary>An entry with the target name already exists and cannot be replaced.</summary>
    TargetExists,

    /// <summary>The requested name is not a valid file name.</summary>
    InvalidName,

    /// <summary>The operating system refused access.</summary>
    AccessDenied,

    /// <summary>The source no longer exists.</summary>
    NotFound
}
