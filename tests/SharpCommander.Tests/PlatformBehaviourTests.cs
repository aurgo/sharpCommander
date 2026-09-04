using Avalonia.Input;
using SharpCommander.Desktop.Services;
using SharpCommander.Desktop.Utilities;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Regression tests for the platform-specific decisions the audit found wrong. They cover the pure helpers
/// behind them, which is the part that can be exercised on any host: the volume filter that hid removable
/// drives on Linux, and the shortcut table that bound Control on macOS.
/// </summary>
public class PlatformBehaviourTests
{
    // ---- Linux volume visibility -------------------------------------------------------------------------

    [Theory]
    // udisks2 mounts removable media here on Fedora, RHEL, Arch and openSUSE. A bare "/run" prefix hid all of it.
    [InlineData("/run/media/user/USB", DriveType.Removable)]
    [InlineData("/run/media/user/My Disk", DriveType.Fixed)]
    // Debian and Ubuntu build udisks with --enable-fhs-media.
    [InlineData("/media/user/USB", DriveType.Removable)]
    [InlineData("/", DriveType.Fixed)]
    [InlineData("/home", DriveType.Fixed)]
    [InlineData("/mnt/data", DriveType.Fixed)]
    // A mounted DVD or a loop-mounted ISO reports CDRom; a filesystem .NET cannot name reports Unknown.
    [InlineData("/media/user/DVD", DriveType.CDRom)]
    [InlineData("/mnt/bcachefs", DriveType.Unknown)]
    public void IsLinuxVolumeVisible_ShowsVolumesThatCarryUserData(string mountPoint, DriveType driveType)
    {
        Assert.True(VolumeEnumerator.IsLinuxVolumeVisible(mountPoint, driveType));
    }

    [Theory]
    [InlineData("/proc", DriveType.Unknown)]
    [InlineData("/sys/fs/cgroup", DriveType.Unknown)]
    [InlineData("/dev/shm", DriveType.Ram)]
    [InlineData("/snap/firefox/1234", DriveType.Fixed)]
    [InlineData("/boot/efi", DriveType.Fixed)]
    [InlineData("/var/lib/docker/overlay2", DriveType.Fixed)]
    // The noisy children of /run stay hidden even though /run itself is no longer a blanket prefix.
    [InlineData("/run/user/1000/gvfs", DriveType.Network)]
    [InlineData("/run/systemd/inaccessible", DriveType.Fixed)]
    [InlineData("/run/snapd/ns", DriveType.Fixed)]
    // Every running AppImage self-mounts here; the dot prefix is the convention that marks it.
    [InlineData("/tmp/.mount_Obsidian1a2b3c", DriveType.Fixed)]
    public void IsLinuxVolumeVisible_HidesSystemAndSelfMountedNoise(string mountPoint, DriveType driveType)
    {
        Assert.False(VolumeEnumerator.IsLinuxVolumeVisible(mountPoint, driveType));
    }

    // ---- command modifier --------------------------------------------------------------------------------

    [Fact]
    public void Shortcuts_CarryTheCommandModifierConsistently()
    {
        // Which modifier the platform reports is not asserted here: Shortcuts asks Avalonia, and under the
        // headless backend that answer is Control even on macOS, so a host-derived expectation would be wrong
        // in CI and right nowhere useful. Whether a real Mac gets Cmd cannot be observed from a headless test -
        // that was the original finding's own caveat. What matters, and what is checkable anywhere, is that
        // every command gesture agrees with whatever the modifier turned out to be: the bug was a dispatch
        // table hardcoded to Ctrl while this class computed something else and had no callers at all.
        Assert.True(Shortcuts.CommandModifier is KeyModifiers.Control or KeyModifiers.Meta);

        foreach (var gesture in new[]
                 {
                     Shortcuts.SelectAll, Shortcuts.Copy, Shortcuts.Cut, Shortcuts.Paste, Shortcuts.Refresh,
                     Shortcuts.ToggleFavoritesPanel, Shortcuts.ToggleFavorite, Shortcuts.Filter,
                     Shortcuts.ShowHiddenFiles, Shortcuts.AdvancedSearch, Shortcuts.MassRename,
                     Shortcuts.Checksums, Shortcuts.NewTab, Shortcuts.CloseTab, Shortcuts.NextTab,
                     Shortcuts.PreviousTab
                 })
        {
            Assert.True(gesture.KeyModifiers.HasFlag(Shortcuts.CommandModifier), $"{gesture} misses the command modifier");
        }
    }

    [Fact]
    public void WithControl_SwapsTheCommandModifierForControlAndKeepsTheRest()
    {
        var cmdShiftF = new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Shift);

        var alias = Shortcuts.WithControl(cmdShiftF);

        Assert.Equal(Key.F, alias.Key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, alias.KeyModifiers);
    }

    [Fact]
    public void Describe_NamesTheKeyTheUserActuallyPresses()
    {
        Assert.Equal("Ctrl+Shift+F", Shortcuts.Describe(new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.Equal("Cmd+C", Shortcuts.Describe(new KeyGesture(Key.C, KeyModifiers.Meta)));
    }
}
