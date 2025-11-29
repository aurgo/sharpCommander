namespace SharpCommander.Core.Models;

/// <summary>
/// Represents the progress of a file operation.
/// </summary>
public sealed record FileOperationProgress
{
    public required string CurrentFile { get; init; }
    public required FileOperationState State { get; init; }
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public long TotalBytes { get; init; }
    public long ProcessedBytes { get; init; }
    
    public double PercentComplete => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;
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
