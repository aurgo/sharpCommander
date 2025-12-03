using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SharpCommander.Desktop.ViewModels;

public partial class HashViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _md5 = string.Empty;

    [ObservableProperty]
    private string _sha1 = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    private string _allHashes = string.Empty;

    [ObservableProperty]
    private bool _isCalculating;

    public HashViewModel(string filePath)
    {
        FilePath = filePath;
        _ = CalculateHashesAsync();
    }

    private async Task CalculateHashesAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            AllHashes = "File not found.";
            return;
        }

        try
        {
            IsCalculating = true;
            AllHashes = "Calculating...";

            await Task.Run(() =>
            {
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(FilePath);

                const int BufferSize = 8192;
                var buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                md5.TransformFinalBlock(buffer, 0, 0);
                sha1.TransformFinalBlock(buffer, 0, 0);
                sha256.TransformFinalBlock(buffer, 0, 0);

                Md5 = BitConverter.ToString(md5.Hash!).Replace("-", "");
                Sha1 = BitConverter.ToString(sha1.Hash!).Replace("-", "");
                Sha256 = BitConverter.ToString(sha256.Hash!).Replace("-", "");

                var sb = new StringBuilder();
                sb.AppendLine($"File: {FilePath}");
                sb.AppendLine();
                sb.AppendLine($"MD5: {Md5}");
                sb.AppendLine($"SHA1: {Sha1}");
                sb.AppendLine($"SHA256: {Sha256}");

                AllHashes = sb.ToString();
            });
        }
        catch (Exception ex)
        {
            AllHashes = $"Error calculating hashes: {ex.Message}";
        }
        finally
        {
            IsCalculating = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        // This command will be bound to the window close action
    }
}
