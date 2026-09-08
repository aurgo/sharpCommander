using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Keeps one live session per endpoint. Sessions are shared by both panels: connecting twice to the same server
/// would double the load on it for no benefit, and a path names its endpoint, so sharing is the natural fit.
/// </summary>
public sealed class SftpConnections(Func<ISftpService> factory) : ISftpConnections, IDisposable
{
    private readonly Dictionary<string, ISftpService> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<ISftpService> _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public SftpConnections()
        : this(() => new SftpService())
    {
    }

    public IReadOnlyList<string> Connected => [.. _sessions.Keys];

    public event EventHandler? Changed;

    public ISftpService? Find(SftpAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return _sessions.TryGetValue(address.Endpoint, out var session) && session.IsConnected ? session : null;
    }

    public ISftpService Require(SftpAddress address)
    {
        return Find(address)
            ?? throw new InvalidOperationException($"Not connected to {address.Endpoint}. Open the connection first.");
    }

    public async Task<string> ConnectAsync(SftpSite site, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        var endpoint = $"{site.Username}@{site.Host}:{site.Port}";
        Disconnect(endpoint);

        var session = _factory();
        try
        {
            await session.ConnectAsync(site, password, cancellationToken);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _sessions[endpoint] = session;
        Changed?.Invoke(this, EventArgs.Empty);

        var start = await session.GetStartDirectoryAsync(cancellationToken);
        return new SftpAddress(site.Username, site.Host, site.Port, start).ToString();
    }

    public void Disconnect(string endpoint)
    {
        if (!_sessions.Remove(endpoint, out var session))
        {
            return;
        }

        session.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DisconnectAll()
    {
        if (_sessions.Count == 0)
        {
            return;
        }

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => DisconnectAll();
}
