namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Stores passwords in the operating system's own keychain. Nothing secret is ever written to the settings file:
/// that file is plain JSON in the user's profile, readable by anything running as them.
/// </summary>
public interface ISecretStore
{
    /// <summary>True when this platform has a keychain this store can use.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads a secret, or null when there is none (or the keychain refused).</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes a secret, replacing any earlier one under the same key.</summary>
    Task SetAsync(string key, string secret, CancellationToken cancellationToken = default);

    /// <summary>Removes a secret; succeeds whether or not one was there.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
