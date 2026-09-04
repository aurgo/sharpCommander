namespace SharpCommander.Core.Models;

/// <summary>
/// Raised by the file system service for failures it can classify (see <see cref="FileOperationErrorKind"/>).
/// </summary>
public sealed class FileOperationException : IOException
{
    /// <summary>Gets the failure category.</summary>
    public FileOperationErrorKind Kind { get; }

    /// <summary>Gets the path the failure refers to.</summary>
    public string Path { get; }

    public FileOperationException(FileOperationErrorKind kind, string path, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Path = path;
    }
}
