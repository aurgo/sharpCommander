using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

public interface IClipboardService
{
    void Copy(IEnumerable<FileSystemEntry> items);
    void Cut(IEnumerable<FileSystemEntry> items);
    IReadOnlyList<FileSystemEntry> GetItems();
    bool IsCutMode { get; }
    void Clear();
}

public class ClipboardService : IClipboardService
{
    private List<FileSystemEntry> _items = new();
    private bool _isCutMode = false;

    public void Copy(IEnumerable<FileSystemEntry> items)
    {
        _items = items.ToList();
        _isCutMode = false;
    }

    public void Cut(IEnumerable<FileSystemEntry> items)
    {
        _items = items.ToList();
        _isCutMode = true;
    }

    public IReadOnlyList<FileSystemEntry> GetItems() => _items;

    public bool IsCutMode => _isCutMode;

    public void Clear()
    {
        _items.Clear();
        _isCutMode = false;
    }
}
