using Avalonia.Controls;
using Avalonia.Interactivity;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Views;

/// <summary>
/// Edits attributes and, off Windows, permissions for one or more entries. The attribute boxes are three-state
/// and start indeterminate, which is what "leave as it is" means: with several entries selected they may well
/// disagree, and forcing a value on all of them is rarely what the user wants. Permissions are all-or-nothing
/// because a mode is a single number, so they are only applied when explicitly enabled.
/// </summary>
public partial class AttributesDialog : Window
{
    /// <summary>What the user asked for; only meaningful when the dialog was accepted.</summary>
    public AttributeChange Result { get; private set; } = new();

    public AttributesDialog()
    {
        InitializeComponent();
    }

    public AttributesDialog(string prompt, UnixFileMode? currentMode)
        : this()
    {
        PromptText.Text = prompt;

        // The two platforms expose different things: Windows has the attribute flags, Unix has the mode bits.
        WindowsAttributes.IsVisible = OperatingSystem.IsWindows();
        UnixPermissions.IsVisible = !OperatingSystem.IsWindows();

        ReadOnlyBox.IsChecked = null;
        HiddenBox.IsChecked = null;
        ArchiveBox.IsChecked = null;
        SystemBox.IsChecked = null;

        if (currentMode is { } mode)
        {
            OwnerRead.IsChecked = mode.HasFlag(UnixFileMode.UserRead);
            OwnerWrite.IsChecked = mode.HasFlag(UnixFileMode.UserWrite);
            OwnerExecute.IsChecked = mode.HasFlag(UnixFileMode.UserExecute);
            GroupRead.IsChecked = mode.HasFlag(UnixFileMode.GroupRead);
            GroupWrite.IsChecked = mode.HasFlag(UnixFileMode.GroupWrite);
            GroupExecute.IsChecked = mode.HasFlag(UnixFileMode.GroupExecute);
            OtherRead.IsChecked = mode.HasFlag(UnixFileMode.OtherRead);
            OtherWrite.IsChecked = mode.HasFlag(UnixFileMode.OtherWrite);
            OtherExecute.IsChecked = mode.HasFlag(UnixFileMode.OtherExecute);
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Result = new AttributeChange
        {
            ReadOnly = OperatingSystem.IsWindows() ? ReadOnlyBox.IsChecked : null,
            Hidden = OperatingSystem.IsWindows() ? HiddenBox.IsChecked : null,
            Archive = OperatingSystem.IsWindows() ? ArchiveBox.IsChecked : null,
            System = OperatingSystem.IsWindows() ? SystemBox.IsChecked : null,
            UnixMode = !OperatingSystem.IsWindows() && ChangePermissionsBox.IsChecked == true ? BuildMode() : null,
            Recursive = RecursiveBox.IsChecked == true
        };

        Close(true);
    }

    private UnixFileMode BuildMode()
    {
        var mode = UnixFileMode.None;

        Add(OwnerRead, UnixFileMode.UserRead);
        Add(OwnerWrite, UnixFileMode.UserWrite);
        Add(OwnerExecute, UnixFileMode.UserExecute);
        Add(GroupRead, UnixFileMode.GroupRead);
        Add(GroupWrite, UnixFileMode.GroupWrite);
        Add(GroupExecute, UnixFileMode.GroupExecute);
        Add(OtherRead, UnixFileMode.OtherRead);
        Add(OtherWrite, UnixFileMode.OtherWrite);
        Add(OtherExecute, UnixFileMode.OtherExecute);

        return mode;

        void Add(CheckBox box, UnixFileMode flag)
        {
            if (box.IsChecked == true)
            {
                mode |= flag;
            }
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
