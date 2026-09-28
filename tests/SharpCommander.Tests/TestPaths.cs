namespace SharpCommander.Tests;

/// <summary>Creates isolated temp directories for file system tests and cleans them up.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharpcommander-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string relative, string content = "x")
    {
        var full = Full(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Dir(string relative)
    {
        var full = Full(relative);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>
    /// Tests write "left/target"; on Windows that would come back as "...\left/target" while the application,
    /// rightly, reports "...\left\target", so the separators are made the platform's own.
    /// </summary>
    private string Full(string relative) =>
        System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* best effort */ }
    }
}
