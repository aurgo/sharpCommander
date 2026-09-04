using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Wraps a <see cref="ConflictResolver"/> for one operation: remembers an "apply to all" answer, and answers
/// Skip when no resolver was given.
/// </summary>
internal sealed class ConflictPolicy
{
    private readonly ConflictResolver? _resolver;
    private ConflictAction? _actionForAll;

    public ConflictPolicy(ConflictResolver? resolver)
    {
        _resolver = resolver;
    }

    /// <summary>Resolves a conflict, asking the resolver unless an earlier answer applies to all.</summary>
    public async Task<ConflictResolution> ResolveAsync(FileConflict conflict, CancellationToken cancellationToken)
    {
        if (_actionForAll is { } action)
        {
            return new ConflictResolution(action, ApplyToAll: true);
        }

        if (_resolver is null)
        {
            return new ConflictResolution(ConflictAction.Skip);
        }

        var resolution = await _resolver(conflict, cancellationToken);
        if (resolution.ApplyToAll && resolution.Action != ConflictAction.Cancel)
        {
            _actionForAll = resolution.Action;
        }

        return resolution;
    }
}
