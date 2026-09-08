using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// A bounded stack of reversible operations. The limit exists because an undo entry holds paths, not contents:
/// the further back an entry is, the more likely the disk has moved on and undoing it would do the wrong thing.
/// Keeping only the recent ones makes the promise honest.
/// </summary>
public sealed class UndoService : IUndoService
{
    private const int Capacity = 20;

    private readonly LinkedList<UndoAction> _actions = new();

    public UndoAction? Next => _actions.First?.Value;

    public event EventHandler? Changed;

    public void Record(UndoAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.Items.Count == 0)
        {
            return;
        }

        _actions.AddFirst(action);

        while (_actions.Count > Capacity)
        {
            _actions.RemoveLast();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public UndoAction? Take()
    {
        if (_actions.First is not { } first)
        {
            return null;
        }

        _actions.RemoveFirst();
        Changed?.Invoke(this, EventArgs.Empty);
        return first.Value;
    }

    public void Clear()
    {
        if (_actions.Count == 0)
        {
            return;
        }

        _actions.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
