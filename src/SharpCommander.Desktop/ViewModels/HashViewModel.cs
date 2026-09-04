using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Computes MD5, SHA-1, SHA-256 and SHA-512 of a file in a single pass with a 1 MiB buffer. The calculation
/// starts with <see cref="StartAsync"/> (called once the window is visible), reports progress and can be
/// cancelled. Results are assigned on the thread that called <see cref="StartAsync"/>.
/// </summary>
public sealed partial class HashViewModel : ObservableObject
{
    private const int BufferSize = 1024 * 1024;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(50);

    private CancellationTokenSource? _cts;
    private bool _started;

    /// <summary>Gets the file being hashed.</summary>
    public string FilePath { get; }

    /// <summary>Gets the file name without its directory.</summary>
    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    private string _md5 = string.Empty;

    [ObservableProperty]
    private string _sha1 = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    private string _sha512 = string.Empty;

    /// <summary>All hashes as a text block ready to paste.</summary>
    [ObservableProperty]
    private string _allHashes = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCalculating;

    [ObservableProperty]
    private bool _hasResults;

    /// <summary>Percentage of the file read so far.</summary>
    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public HashViewModel(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        FilePath = filePath;
    }

    /// <summary>
    /// Computes the four hashes. Never throws: failures end up in <see cref="ErrorMessage"/>. A second call is
    /// ignored.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        if (!File.Exists(FilePath))
        {
            Fail("File not found.");
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsCalculating = true;
        HasError = false;
        StatusText = "Calculating...";

        // Progress<T> posts to the synchronization context captured here, i.e. the UI thread.
        var progress = new Progress<double>(percent =>
        {
            ProgressPercent = percent;
            StatusText = $"Calculating... {percent:0}%";
        });

        try
        {
            var result = await Task.Run(() => ComputeAsync(FilePath, progress, cts.Token), cts.Token);

            Md5 = result.Md5;
            Sha1 = result.Sha1;
            Sha256 = result.Sha256;
            Sha512 = result.Sha512;
            AllHashes = FormatAll(result);
            ProgressPercent = 100;
            HasResults = true;
            StatusText = "Done";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            Fail($"Error calculating hashes: {ex.Message}");
        }
        finally
        {
            _cts = null;
            IsCalculating = false;
        }
    }

    /// <summary>Stops the calculation. Safe to call when nothing is running.</summary>
    [RelayCommand(CanExecute = nameof(IsCalculating))]
    public void Cancel()
    {
        _cts?.Cancel();
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
        StatusText = "Failed";
    }

    private string FormatAll(HashResult result)
    {
        var builder = new StringBuilder();
        builder.Append("File: ").AppendLine(FilePath);
        builder.Append("MD5: ").AppendLine(result.Md5);
        builder.Append("SHA-1: ").AppendLine(result.Sha1);
        builder.Append("SHA-256: ").AppendLine(result.Sha256);
        builder.Append("SHA-512: ").Append(result.Sha512);
        return builder.ToString();
    }

    private static async Task<HashResult> ComputeAsync(string path, IProgress<double> progress, CancellationToken token)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var total = stream.Length;
        long done = 0;
        var lastReport = Stopwatch.StartNew();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), token).ConfigureAwait(false)) > 0)
            {
                md5.AppendData(buffer, 0, read);
                sha1.AppendData(buffer, 0, read);
                sha256.AppendData(buffer, 0, read);
                sha512.AppendData(buffer, 0, read);

                done += read;
                if (lastReport.Elapsed >= ProgressInterval)
                {
                    progress.Report(total > 0 ? done * 100.0 / total : 100);
                    lastReport.Restart();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new HashResult(Hex(md5), Hex(sha1), Hex(sha256), Hex(sha512));
    }

    private static string Hex(IncrementalHash hash)
    {
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private readonly record struct HashResult(string Md5, string Sha1, string Sha256, string Sha512);
}
