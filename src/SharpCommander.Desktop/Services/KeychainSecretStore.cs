using System.Diagnostics;
using System.Text;
using SharpCommander.Core.Interfaces;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Passwords in the platform keychain. macOS is driven through the "security" command rather than a native
/// binding: it ships with the system, needs no P/Invoke that trimming or AOT could break, and shows the user the
/// familiar keychain prompt when access needs approving.
///
/// Writing goes through "security -i", which reads whole commands from standard input: passing the password as
/// a normal argument would put it in the process list, where every process on the machine can read it. Reading
/// needs no secret on the way in, so it uses the plain form.
///
/// Where no keychain is reachable — Linux without a running secret service, or a locked keychain — the store
/// reports itself unavailable and the caller asks for the password each time instead of storing it badly.
/// </summary>
public sealed class KeychainSecretStore : ISecretStore
{
    private const string ServiceName = "SharpCommander";

    /// <summary>
    /// How long to wait for the keychain tool. The keychain can put up its own permission dialog, and if the user
    /// never answers it the tool never exits: without a deadline that would hang whoever asked for the password.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    public bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (OperatingSystem.IsMacOS())
        {
            var result = await RunAsync(["find-generic-password", "-s", ServiceName, "-a", key, "-w"], null, cancellationToken);
            return result.ExitCode == 0 ? result.Output.TrimEnd('\n') : null;
        }

        if (OperatingSystem.IsWindows())
        {
            return await WindowsCredentialFile.ReadAsync(key, cancellationToken);
        }

        return null;
    }

    public async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(secret);

        if (OperatingSystem.IsMacOS())
        {
            // -U replaces an existing entry. The command text goes in on stdin so the secret never becomes an
            // argument; security's own parser handles the quoting, so quotes and backslashes are escaped for it.
            var command = $"add-generic-password -U -s {Quote(ServiceName)} -a {Quote(key)} -w {Quote(secret)}\n";
            var result = await RunAsync(["-i"], command, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"The password could not be saved to the keychain: {result.Error.Trim()}");
            }

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await WindowsCredentialFile.WriteAsync(key, secret, cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException("This platform has no keychain SharpCommander can use.");
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (OperatingSystem.IsMacOS())
        {
            // A missing entry exits non-zero; removing what is not there is not an error for us.
            await RunAsync(["delete-generic-password", "-s", ServiceName, "-a", key], null, cancellationToken);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await WindowsCredentialFile.RemoveAsync(key, cancellationToken);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>Quotes a value for the argument parser inside "security -i".</summary>
    private static string Quote(string value) => '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] arguments, string? stdin, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The keychain tool could not be started.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        try
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), deadline.Token);
                process.StandardInput.Close();
            }

            // Both streams are drained together: reading one to the end while the other fills its buffer is the
            // classic way to deadlock on a child process.
            var outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(deadline.Token);

            return (process.ExitCode, outputTask.Result, errorTask.Result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("The keychain did not answer. It may be locked, or waiting for permission.");
        }
    }
}

/// <summary>
/// Windows fallback: the secret is protected with DPAPI (current user) and kept beside the settings. DPAPI ties
/// the ciphertext to the Windows account, so another user on the same machine cannot read it back.
/// </summary>
internal static class WindowsCredentialFile
{
    private static string PathFor(string key)
    {
        var safe = Convert.ToHexString(Encoding.UTF8.GetBytes(key));
        return Path.Combine(AppPaths.ConfigDirectory, "secrets", safe + ".bin");
    }

    public static async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return Encoding.UTF8.GetString(Unprotect(protectedBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            AppLog.Warning($"The stored password for '{key}' could not be read.", ex);
            return null;
        }
    }

    public static async Task WriteAsync(string key, string secret, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Protect(Encoding.UTF8.GetBytes(secret)), cancellationToken);
    }

    public static Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? System.Security.Cryptography.ProtectedData.Protect(data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser) : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? System.Security.Cryptography.ProtectedData.Unprotect(data, null, System.Security.Cryptography.DataProtectionScope.CurrentUser) : data;
}
