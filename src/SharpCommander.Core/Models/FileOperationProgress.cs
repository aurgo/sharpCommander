namespace SharpCommander.Core.Models;

/// <summary>
/// Represents the progress of a file operation. Byte counters cover the whole operation
/// (all files of a directory tree); file counters advance once per file, skipped files included.
/// </summary>
public sealed record FileOperationProgress
{
    public required string CurrentFile { get; init; }
    public required FileOperationState State { get; init; }
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public long TotalBytes { get; init; }
    public long ProcessedBytes { get; init; }

    /// <summary>
    /// Completion in percent: by bytes when the total is known, otherwise by file count; 100 once completed.
    /// </summary>
    public double PercentComplete
    {
        get
        {
            if (State == FileOperationState.Completed)
            {
                return 100;
            }

            var ratio = TotalBytes > 0
                ? (double)ProcessedBytes / TotalBytes
                : TotalFiles > 0
                    ? (double)ProcessedFiles / TotalFiles
                    : 0;

            return Math.Clamp(ratio * 100, 0, 100);
        }
    }
}

/// <summary>
/// State of a file operation.
/// </summary>
public enum FileOperationState
{
    Starting,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
