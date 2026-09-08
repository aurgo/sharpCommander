using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Remembers the last few reversible operations. Nothing here touches the disk on its own: the view model asks
/// for the next action and performs the reversal, so undoing goes through the same file system service — and the
/// same permission and error handling — as everything else.
/// </summary>
public interface IUndoService
{
    /// <summary>The action that would be undone next, or null when there is nothing to undo.</summary>
    UndoAction? Next { get; }

    /// <summary>Raised whenever <see cref="Next"/> changes, so a menu can enable or disable itself.</summary>
    event EventHandler? Changed;

    /// <summary>Pushes a completed operation. An action with no items is ignored.</summary>
    void Record(UndoAction action);

    /// <summary>Removes and returns the next action, or null when there is none.</summary>
    UndoAction? Take();

    /// <summary>Forgets everything, for when the history can no longer be trusted.</summary>
    void Clear();
}
