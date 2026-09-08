namespace SharpCommander.Core.Models;

/// <summary>What kind of operation an undo entry reverses.</summary>
public enum UndoActionKind
{
    /// <summary>Files were moved; undoing moves them back.</summary>
    Move,

    /// <summary>Files were copied; undoing deletes the copies that were created.</summary>
    Copy,

    /// <summary>An entry was renamed; undoing renames it back.</summary>
    Rename,

    /// <summary>A folder was created; undoing removes it when it is still empty.</summary>
    CreateFolder
}

/// <summary>One thing that moved or was created, as "it used to be at From, it is now at To".</summary>
public sealed record UndoItem(string From, string To);

/// <summary>
/// A completed operation that can be reversed. Deletion is deliberately absent: the platform trash offers no
/// restore API, so an entry that claimed to undo a deletion could not keep its promise.
/// </summary>
public sealed record UndoAction
{
    public required UndoActionKind Kind { get; init; }

    /// <summary>What to show the user, as in "Undo move of 3 items".</summary>
    public required string Description { get; init; }

    /// <summary>
    /// For a move or rename, where each entry came from and went to. For a copy or a new folder, only the
    /// entries that were actually created, so undoing never deletes something that was already there.
    /// </summary>
    public required IReadOnlyList<UndoItem> Items { get; init; }

    /// <summary>True when undoing this deletes files, which needs the user's confirmation.</summary>
    public bool IsDestructive => Kind is UndoActionKind.Copy or UndoActionKind.CreateFolder;
}
