namespace SharpCommander.Core.Models;

/// <summary>How a name found while comparing two folders differs between them.</summary>
public enum ComparisonState
{
    /// <summary>Present on both sides with the same size and write time.</summary>
    Same,

    /// <summary>Present on both sides but not identical.</summary>
    Different,

    /// <summary>Only on the left.</summary>
    OnlyLeft,

    /// <summary>Only on the right.</summary>
    OnlyRight
}

/// <summary>One name as found on both sides of a comparison.</summary>
public sealed record ComparisonItem
{
    public required string Name { get; init; }

    public required ComparisonState State { get; init; }

    /// <summary>True when the name is a folder on the side (or sides) it exists on.</summary>
    public bool IsDirectory { get; init; }

    public long? LeftSize { get; init; }

    public long? RightSize { get; init; }
}

/// <summary>The result of comparing two folders, one entry per name found on either side.</summary>
public sealed record DirectoryComparison
{
    public required string LeftPath { get; init; }

    public required string RightPath { get; init; }

    public required IReadOnlyList<ComparisonItem> Items { get; init; }

    public int OnlyLeft => Items.Count(item => item.State == ComparisonState.OnlyLeft);

    public int OnlyRight => Items.Count(item => item.State == ComparisonState.OnlyRight);

    public int Different => Items.Count(item => item.State == ComparisonState.Different);

    public int Same => Items.Count(item => item.State == ComparisonState.Same);
}
