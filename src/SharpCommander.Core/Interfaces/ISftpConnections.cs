using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// The open SFTP sessions, one per endpoint. Paths carry the endpoint in them, so anything holding a path can
/// find the session it belongs to without also having to carry the connection around.
/// </summary>
public interface ISftpConnections
{
    /// <summary>Endpoints currently connected, as "user@host:port".</summary>
    IReadOnlyList<string> Connected { get; }

    /// <summary>Raised when a connection opens or closes.</summary>
    event EventHandler? Changed;

    /// <summary>The session for an address, or null when that endpoint is not connected.</summary>
    ISftpService? Find(SftpAddress address);

    /// <summary>The session for an address, or an error explaining that it is not connected.</summary>
    ISftpService Require(SftpAddress address);

    /// <summary>Opens a session, replacing any earlier one for the same endpoint. Returns its root path.</summary>
    Task<string> ConnectAsync(SftpSite site, string? password, CancellationToken cancellationToken = default);

    /// <summary>Closes one session; unknown endpoints are ignored.</summary>
    void Disconnect(string endpoint);

    void DisconnectAll();
}
