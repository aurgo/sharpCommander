using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Looks for a newer published release. It only ever reads: downloading and replacing the running application
/// needs code signing, elevated permissions and an atomic swap that differ on every platform, and a half-done
/// version of that leaves an installation nobody can repair. The user is told and taken to the download page.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Asks the server for the latest release and compares it with <paramref name="currentVersion"/>. Returns
    /// null when the server could not be reached or answered with something unusable — a failed check is not
    /// worth interrupting anyone over.
    /// </summary>
    Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}
