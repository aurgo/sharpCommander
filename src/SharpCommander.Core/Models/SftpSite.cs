namespace SharpCommander.Core.Models;

/// <summary>How to prove who we are to the server.</summary>
public enum SftpAuthentication
{
    /// <summary>A private key from ~/.ssh, chosen by the agent or by <see cref="SftpSite.KeyPath"/>.</summary>
    PrivateKey,

    /// <summary>A password, kept in the operating system keychain and never in the settings file.</summary>
    Password
}

/// <summary>
/// A saved SFTP server. Only what is safe to write to disk lives here: the password never does, it is stored in
/// the platform keychain under <see cref="CredentialKey"/> and read back when connecting.
/// </summary>
public sealed class SftpSite
{
    /// <summary>What to call it in the interface; defaults to "user@host" when left empty.</summary>
    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public SftpAuthentication Authentication { get; set; } = SftpAuthentication.PrivateKey;

    /// <summary>Private key file; empty means the usual candidates in ~/.ssh are tried in turn.</summary>
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>Folder to open on connecting; empty means the server's default (usually the home folder).</summary>
    public string InitialPath { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}" : Name;

    /// <summary>
    /// The keychain entry this site's password lives under. Built from host, port and user so two accounts on
    /// one server, or one account on two ports, never share a secret.
    /// </summary>
    public string CredentialKey => $"sftp://{Username}@{Host}:{Port}";
}
