using System.Text;
using Avalonia;
using Avalonia.Input;

namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// The keyboard shortcuts that use the platform's command modifier, built once from what the platform reports
/// (Cmd on macOS, Ctrl on Windows and Linux) so the key dispatcher of the main window, the menus and the
/// tooltips all agree. Function keys are the same everywhere and are not listed here.
/// </summary>
public static class Shortcuts
{
    /// <summary>The modifier the platform uses for application commands: Meta (Cmd) on macOS, Control elsewhere.</summary>
    public static KeyModifiers CommandModifier { get; } = DetectCommandModifier();

    public static KeyGesture SelectAll { get; } = new(Key.A, CommandModifier);
    public static KeyGesture Copy { get; } = new(Key.C, CommandModifier);
    public static KeyGesture Cut { get; } = new(Key.X, CommandModifier);
    public static KeyGesture Paste { get; } = new(Key.V, CommandModifier);
    public static KeyGesture Refresh { get; } = new(Key.R, CommandModifier);
    public static KeyGesture ToggleFavoritesPanel { get; } = new(Key.B, CommandModifier);
    public static KeyGesture ToggleFavorite { get; } = new(Key.D, CommandModifier);
    public static KeyGesture Filter { get; } = new(Key.F, CommandModifier);
    public static KeyGesture ShowHiddenFiles { get; } = new(Key.H, CommandModifier);
    public static KeyGesture AdvancedSearch { get; } = new(Key.F, CommandModifier | KeyModifiers.Shift);
    public static KeyGesture MassRename { get; } = new(Key.M, CommandModifier);
    public static KeyGesture Checksums { get; } = new(Key.H, CommandModifier | KeyModifiers.Shift);
    public static KeyGesture NewTab { get; } = new(Key.T, CommandModifier);
    public static KeyGesture CloseTab { get; } = new(Key.W, CommandModifier);
    public static KeyGesture NextTab { get; } = new(Key.Tab, CommandModifier);
    public static KeyGesture PreviousTab { get; } = new(Key.Tab, CommandModifier | KeyModifiers.Shift);

    /// <summary>Tooltip texts that mention a shortcut, so the XAML shows the platform's key.</summary>
    public static string RefreshPanelsToolTip { get; } = $"Refresh both panels ({Describe(Refresh)})";
    public static string RefreshToolTip { get; } = $"Refresh ({Describe(Refresh)})";
    public static string FavoritesPanelToolTip { get; } = $"Toggle the favorites panel ({Describe(ToggleFavoritesPanel)})";
    public static string ToggleFavoriteToolTip { get; } = $"Toggle favorite ({Describe(ToggleFavorite)})";
    public static string FilterToolTip { get; } = $"Filter ({Describe(Filter)})";
    public static string NewTabToolTip { get; } = $"New tab ({Describe(NewTab)})";
    public static string CloseTabToolTip { get; } = $"Close tab ({Describe(CloseTab)})";

    /// <summary>Formats a gesture for display: "Ctrl+Shift+F", "Cmd+C".</summary>
    public static string Describe(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        var text = new StringBuilder();
        Append(KeyModifiers.Control, "Ctrl");
        Append(KeyModifiers.Meta, "Cmd");
        Append(KeyModifiers.Alt, "Alt");
        Append(KeyModifiers.Shift, "Shift");
        text.Append(gesture.Key);
        return text.ToString();

        void Append(KeyModifiers modifier, string name)
        {
            if (gesture.KeyModifiers.HasFlag(modifier))
            {
                text.Append(name).Append('+');
            }
        }
    }

    /// <summary>Returns the same gesture with Control in place of the command modifier (the alias accepted on macOS).</summary>
    public static KeyGesture WithControl(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        return new KeyGesture(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Meta) | KeyModifiers.Control);
    }

    private static KeyModifiers DetectCommandModifier()
    {
        try
        {
            if (Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers is { } modifiers && modifiers != KeyModifiers.None)
            {
                return modifiers;
            }
        }
        catch (Exception)
        {
            // No platform yet (design time): fall through to the platform default.
        }

        return OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    }
}
