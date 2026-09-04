using System.Collections;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Desktop.Utilities;

namespace SharpCommander.Desktop.ViewModels;

/// <summary>
/// Read-only file viewer (F3): text with encoding detection (BOM, then strict UTF-8, then Latin-1) or a
/// virtualized hex dump for binary files. Only the first <see cref="MaxBytes"/> of larger files are loaded.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    /// <summary>Largest amount of a file that is loaded into the viewer.</summary>
    public const long MaxBytes = 16L * 1024 * 1024;

    private const int BinaryProbeLength = 8 * 1024;

    private CancellationTokenSource? _cts;
    private bool _started;
    private int _lastMatchEnd;

    /// <summary>Gets the file shown.</summary>
    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Decoded text (empty for binary files).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand))]
    private bool _isBinary;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _encodingName = string.Empty;

    [ObservableProperty]
    private int _lineCount;

    [ObservableProperty]
    private long _fileSize;

    /// <summary>True when the file is larger than <see cref="MaxBytes"/> and only its start is shown.</summary>
    [ObservableProperty]
    private bool _isTruncated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindNextCommand))]
    private string _findText = string.Empty;

    [ObservableProperty]
    private string _findStatus = string.Empty;

    /// <summary>Start of the current find match in <see cref="Text"/>, or -1.</summary>
    [ObservableProperty]
    private int _matchStart = -1;

    [ObservableProperty]
    private int _matchLength;

    /// <summary>Lines of the hex dump; formatted on demand so only the visible rows are materialized.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _hexLines = Array.Empty<string>();

    /// <summary>Raised after every successful <see cref="FindNextCommand"/> with the match offset.</summary>
    public event EventHandler<int>? MatchFound;

    public ViewerViewModel(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        FilePath = filePath;
    }

    /// <summary>Reads and decodes the file on a background thread. Never throws; a second call is ignored.</summary>
    public async Task LoadAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsLoading = true;
        StatusText = "Loading...";

        try
        {
            var content = await Task.Run(() => Load(FilePath, cts.Token), cts.Token);

            FileSize = content.FileSize;
            IsTruncated = content.IsTruncated;
            IsBinary = content.IsBinary;
            EncodingName = content.EncodingName;
            Text = content.Text;
            LineCount = content.LineCount;
            HexLines = content.HexLines;
            _lastMatchEnd = 0;
            StatusText = BuildStatus();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot open the file: {ex.Message}";
        }
        finally
        {
            _cts = null;
            IsLoading = false;
        }
    }

    /// <summary>Stops loading. Safe to call at any time.</summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanFindNext() => !IsBinary && Text.Length > 0 && !string.IsNullOrEmpty(FindText);

    /// <summary>Selects the next case-insensitive occurrence of <see cref="FindText"/>, wrapping around.</summary>
    [RelayCommand(CanExecute = nameof(CanFindNext))]
    private void FindNext()
    {
        var text = Text;
        var query = FindText;
        var start = _lastMatchEnd >= text.Length ? 0 : _lastMatchEnd;
        var wrapped = false;

        var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            wrapped = true;
        }

        if (index < 0)
        {
            _lastMatchEnd = 0;
            MatchStart = -1;
            MatchLength = 0;
            FindStatus = "Not found";
            return;
        }

        _lastMatchEnd = index + query.Length;
        MatchStart = index;
        MatchLength = query.Length;
        FindStatus = wrapped ? "Wrapped to the beginning" : string.Empty;
        MatchFound?.Invoke(this, index);
    }

    partial void OnFindTextChanged(string value)
    {
        _lastMatchEnd = 0;
        FindStatus = string.Empty;
    }

    private string BuildStatus()
    {
        var parts = new List<string> { $"{FileSizeFormatter.Format(FileSize)} ({FileSize:N0} bytes)" };
        if (IsBinary)
        {
            parts.Add("Binary (hex view)");
        }
        else
        {
            parts.Add(EncodingName);
            parts.Add(LineCount == 1 ? "1 line" : $"{LineCount:N0} lines");
        }

        if (IsTruncated)
        {
            parts.Add($"only the first {FileSizeFormatter.Format(MaxBytes)} are shown");
        }

        return string.Join("  |  ", parts);
    }

    private static ViewerContent Load(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
        var fileSize = stream.Length;
        var length = (int)Math.Min(fileSize, MaxBytes);
        var bytes = new byte[length];

        var read = 0;
        while (read < length)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, read, length - read);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return Decode(bytes, read, fileSize, fileSize > length);
    }

    /// <summary>Decodes a buffer the way the viewer does; exposed for tests.</summary>
    internal static ViewerContent Decode(byte[] bytes, int length, long fileSize, bool truncated)
    {
        var (encoding, bomLength, name) = DetectBom(bytes, length);
        if (encoding is null)
        {
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(length, BinaryProbeLength)) >= 0)
            {
                return new ViewerContent(string.Empty, "Binary", true, fileSize, truncated, 0, new HexDumpLines(bytes, length));
            }

            var textLength = truncated ? TrimIncompleteUtf8Tail(bytes, length) : length;
            try
            {
                var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                return CreateText(strict.GetString(bytes, 0, textLength), "UTF-8", fileSize, truncated);
            }
            catch (DecoderFallbackException)
            {
                return CreateText(Encoding.Latin1.GetString(bytes, 0, length), "Latin-1 (ISO 8859-1)", fileSize, truncated);
            }
        }

        return CreateText(encoding.GetString(bytes, bomLength, length - bomLength), name, fileSize, truncated);
    }

    private static ViewerContent CreateText(string text, string encodingName, long fileSize, bool truncated)
    {
        var lines = text.Length == 0 ? 0 : text.AsSpan().Count('\n') + 1;
        return new ViewerContent(text, encodingName, false, fileSize, truncated, lines, Array.Empty<string>());
    }

    private static (Encoding? Encoding, int BomLength, string Name) DetectBom(byte[] bytes, int length)
    {
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(false), 3, "UTF-8 with BOM");
        }

        if (length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            return (new UTF32Encoding(false, false), 4, "UTF-32 LE");
        }

        if (length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(true, false), 4, "UTF-32 BE");
        }

        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(false, false), 2, "UTF-16 LE");
        }

        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(true, false), 2, "UTF-16 BE");
        }

        return (null, 0, string.Empty);
    }

    /// <summary>
    /// Drops the bytes of a multi-byte sequence cut by the size cap so a truncated UTF-8 file still decodes.
    /// </summary>
    private static int TrimIncompleteUtf8Tail(byte[] bytes, int length)
    {
        for (var back = 1; back <= 4 && back <= length; back++)
        {
            var b = bytes[length - back];
            if ((b & 0xC0) == 0x80)
            {
                continue; // continuation byte, keep looking for the lead
            }

            var expected = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return expected > back ? length - back : length;
        }

        return length;
    }
}

/// <summary>Result of decoding a file for the viewer.</summary>
internal sealed record ViewerContent(
    string Text,
    string EncodingName,
    bool IsBinary,
    long FileSize,
    bool IsTruncated,
    int LineCount,
    IReadOnlyList<string> HexLines);

/// <summary>
/// Hex dump lines ("offset  hex bytes  |ascii|") formatted lazily from the raw bytes. Implements the
/// non-generic <see cref="IList"/> so virtualizing lists index it without copying.
/// </summary>
public sealed class HexDumpLines : IReadOnlyList<string>, IList
{
    /// <summary>Bytes shown per line.</summary>
    public const int BytesPerLine = 16;

    private readonly byte[] _bytes;
    private readonly int _length;

    public HexDumpLines(byte[] bytes, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, bytes.Length);
        _bytes = bytes;
        _length = length;
    }

    public int Count => (_length + BytesPerLine - 1) / BytesPerLine;

    public string this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return FormatLine(index * BytesPerLine);
        }
    }

    private string FormatLine(int offset)
    {
        var count = Math.Min(BytesPerLine, _length - offset);
        var builder = new StringBuilder(80);
        builder.Append(offset.ToString("X8")).Append("  ");

        for (var i = 0; i < BytesPerLine; i++)
        {
            if (i == 8)
            {
                builder.Append(' ');
            }

            if (i < count)
            {
                builder.Append(_bytes[offset + i].ToString("X2")).Append(' ');
            }
            else
            {
                builder.Append("   ");
            }
        }

        builder.Append(" |");
        for (var i = 0; i < count; i++)
        {
            var b = _bytes[offset + i];
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return builder.Append('|').ToString();
    }

    public IEnumerator<string> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is string s && IndexOf(s) >= 0;
    int IList.IndexOf(object? value) => value is string s ? IndexOf(s) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    void ICollection.CopyTo(Array array, int index)
    {
        for (var i = 0; i < Count; i++)
        {
            array.SetValue(this[i], index + i);
        }
    }

    private int IndexOf(string line)
    {
        // Lines start with their zero-padded offset, so the index can be recovered without a scan.
        if (line.Length >= 8 && int.TryParse(line.AsSpan(0, 8), System.Globalization.NumberStyles.HexNumber, null, out var offset)
            && offset % BytesPerLine == 0 && offset / BytesPerLine < Count && this[offset / BytesPerLine] == line)
        {
            return offset / BytesPerLine;
        }

        return -1;
    }
}
