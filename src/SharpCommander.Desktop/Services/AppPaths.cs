namespace SharpCommander.Desktop.Services;

/// <summary>
/// Well-known locations of the per-user configuration files. Directories are created on demand.
/// </summary>
public static class AppPaths
{
    private const string ApplicationFolderName = "SharpCommander";

    /// <summary>Gets the configuration directory (ApplicationData/SharpCommander), created if missing.</summary>
    public static string ConfigDirectory => EnsureDirectory(Path.Combine(GetBaseDirectory(), ApplicationFolderName));

    /// <summary>Gets the log directory below <see cref="ConfigDirectory"/>, created if missing.</summary>
    public static string LogsDirectory => EnsureDirectory(Path.Combine(ConfigDirectory, "logs"));

    /// <summary>Gets the full path of settings.json.</summary>
    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    private static string GetBaseDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData) ? Path.GetTempPath() : appData;
    }

    private static string EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller will fail with a clear error when it tries to use the directory.
        }

        return path;
    }
}
