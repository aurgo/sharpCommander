namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Moves entries to the operating system trash (Windows recycle bin, macOS Trash, freedesktop trash on Linux).
/// </summary>
public interface ITrashService
{
    /// <summary>Gets whether the current platform has a trash implementation.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Moves a file or directory to the trash. Throws <see cref="IOException"/> when the platform refused,
    /// and <see cref="PlatformNotSupportedException"/> when <see cref="IsSupported"/> is false.
    /// </summary>
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default);
}
