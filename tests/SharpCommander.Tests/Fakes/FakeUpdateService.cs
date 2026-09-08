using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>An update service that answers whatever a test sets, without touching the network.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    /// <summary>What CheckAsync returns; null stands for "the server could not be reached".</summary>
    public UpdateInfo? Answer { get; set; }

    public int Checks { get; private set; }

    public Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        Checks++;
        return Task.FromResult(Answer);
    }
}
