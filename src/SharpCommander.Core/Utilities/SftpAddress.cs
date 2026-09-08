namespace SharpCommander.Core.Utilities;

/// <summary>
/// A location on a remote server, written as <c>sftp://user@host:port/path</c>. Panels, the operations engine and
/// the settings all pass paths around as plain strings, so remote places have to be expressible as one too:
/// giving them a scheme lets every existing command keep working and lets one router decide, per path, whether
/// the work is local or remote.
/// </summary>
public sealed record SftpAddress(string Username, string Host, int Port, string Path)
{
    public const string Scheme = "sftp://";

    /// <summary>True when a path names a remote place rather than a local one.</summary>
    public static bool IsRemote(string? path) =>
        path is not null && path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>The endpoint alone, without the path: what identifies a connection.</summary>
    public string Endpoint => $"{Username}@{Host}:{Port}";

    /// <summary>Rebuilds the full path.</summary>
    public override string ToString() => $"{Scheme}{Endpoint}{RemotePath.Normalize(Path)}";

    /// <summary>The same address pointing somewhere else on the same server.</summary>
    public SftpAddress With(string path) => this with { Path = RemotePath.Normalize(path) };

    /// <summary>
    /// Parses a remote path. Returns null for anything that is not one, so callers can use it as the test for
    /// "is this remote" and get the parts in the same step.
    /// </summary>
    public static SftpAddress? TryParse(string? path)
    {
        if (!IsRemote(path))
        {
            return null;
        }

        var rest = path![Scheme.Length..];

        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var remotePath = slash < 0 ? RemotePath.Root : rest[slash..];

        var at = authority.LastIndexOf('@');
        if (at <= 0 || at == authority.Length - 1)
        {
            return null;
        }

        var user = authority[..at];
        var hostAndPort = authority[(at + 1)..];

        var colon = hostAndPort.LastIndexOf(':');
        string host;
        var port = 22;

        if (colon > 0)
        {
            host = hostAndPort[..colon];
            if (!int.TryParse(hostAndPort[(colon + 1)..], out port) || port is < 1 or > 65535)
            {
                return null;
            }
        }
        else
        {
            host = hostAndPort;
        }

        return host.Length == 0 ? null : new SftpAddress(user, host, port, RemotePath.Normalize(remotePath));
    }

    /// <summary>Builds the path for a name inside this folder.</summary>
    public string Combine(string name) => With(RemotePath.Combine(Path, name)).ToString();

    /// <summary>The parent of this address, or null when it is already the server's root.</summary>
    public string? Parent()
    {
        var parent = RemotePath.GetParent(Path);
        return parent is null ? null : With(parent).ToString();
    }
}
