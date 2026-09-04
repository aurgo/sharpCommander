using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Minimal thread-safe file logger writing to logs/app.log in the configuration directory.
/// The file is rotated to app.log.1 once it exceeds 1 MiB. Logging never throws.
/// </summary>
public static class AppLog
{
    private const long MaxSizeBytes = 1024 * 1024;
    private const string FileName = "app.log";
    private static readonly object Gate = new();
    private static string? _logFile;

    /// <summary>Gets the full path of the current log file, or null before <see cref="Initialize"/>.</summary>
    public static string? LogFile => _logFile;

    /// <summary>Resolves the log file and writes a startup line. Safe to call more than once.</summary>
    public static void Initialize()
    {
        try
        {
            var file = Path.Combine(AppPaths.LogsDirectory, FileName);
            lock (Gate)
            {
                _logFile = file;
            }
        }
        catch (Exception)
        {
            return;
        }

        var version = typeof(AppLog).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Info($"SharpCommander {version} starting on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}), .NET {Environment.Version}");
    }

    /// <summary>Redirects the log to <paramref name="path"/>, or disables it when null (tests).</summary>
    internal static void UseLogFile(string? path)
    {
        lock (Gate)
        {
            _logFile = path;
        }
    }

    /// <summary>Writes an informational line.</summary>
    public static void Info(string message, Exception? exception = null) => Write("INFO ", message, exception);

    /// <summary>Writes a warning line.</summary>
    public static void Warning(string message, Exception? exception = null) => Write("WARN ", message, exception);

    /// <summary>Writes an error line, with the full exception text when given.</summary>
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                line.AppendLine().Append(exception);
            }

            line.AppendLine();

            lock (Gate)
            {
                if (_logFile is null)
                {
                    return;
                }

                RotateIfNeeded(_logFile);
                File.AppendAllText(_logFile, line.ToString());
            }
        }
        catch (Exception)
        {
            // A logger that throws would hide the original problem.
        }
    }

    private static void RotateIfNeeded(string logFile)
    {
        var info = new FileInfo(logFile);
        if (!info.Exists || info.Length <= MaxSizeBytes)
        {
            return;
        }

        File.Move(logFile, logFile + ".1", overwrite: true);
    }
}
