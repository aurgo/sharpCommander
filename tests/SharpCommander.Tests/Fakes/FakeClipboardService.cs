using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;

namespace SharpCommander.Tests.Fakes;

/// <summary>In-memory clipboard with the same contract as the real service, minus the operating system.</summary>
public sealed class FakeClipboardService : IClipboardService
{
    public List<string> Paths { get; private set; } = [];

    public bool IsCutMode { get; private set; }

    /// <summary>Every call, as "copy:N", "cut:N", "get", "clear".</summary>
    public List<string> Calls { get; } = [];

    public Task CopyAsync(IEnumerable<FileSystemEntry> items)
    {
        Paths = items.Select(item => item.FullPath).ToList();
        IsCutMode = false;
        Calls.Add("copy:" + Paths.Count);
        return Task.CompletedTask;
    }

    public Task CutAsync(IEnumerable<FileSystemEntry> items)
    {
        Paths = items.Select(item => item.FullPath).ToList();
        IsCutMode = Paths.Count > 0;
        Calls.Add("cut:" + Paths.Count);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetPathsAsync()
    {
        Calls.Add("get");
        return Task.FromResult<IReadOnlyList<string>>(Paths.ToList());
    }

    public Task ClearAsync()
    {
        Calls.Add("clear");
        Paths = [];
        IsCutMode = false;
        return Task.CompletedTask;
    }
}
