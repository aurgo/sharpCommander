namespace SharpCommander.Core.Models;

/// <summary>
/// A copy of SpaceAnalyzer that has been downloaded and kept.
/// </summary>
/// <param name="Version">The release it came from.</param>
/// <param name="Path">What is started: the executable on Windows and Linux, the application bundle on macOS.</param>
public sealed record SpaceAnalyzerInstall(Version Version, string Path);
