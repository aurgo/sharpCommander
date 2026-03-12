using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

public interface IDialogService
{
    Task ShowHashDialogAsync(string filePath);
    Task ShowAdvancedSearchDialogAsync(string initialPath);
    Task ShowMassRenameDialogAsync(IEnumerable<string> filePaths);
    Task ShowPropertiesDialogAsync(FileSystemEntry entry);
    Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue = "");
}
