using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Applies the theme stored in the settings ("System", "Light" or "Dark") to the running application.
/// </summary>
public sealed class ThemeService
{
    public const string SystemTheme = "System";
    public const string LightTheme = "Light";
    public const string DarkTheme = "Dark";

    /// <summary>Gets the theme names in menu order.</summary>
    public static IReadOnlyList<string> Themes { get; } = [SystemTheme, LightTheme, DarkTheme];

    /// <summary>Gets the theme applied last, normalized to one of <see cref="Themes"/>.</summary>
    public string CurrentTheme { get; private set; } = SystemTheme;

    /// <summary>Maps any spelling of a theme name to its canonical form; unknown values become "System".</summary>
    public static string Normalize(string? theme)
    {
        var trimmed = theme?.Trim();
        return Themes.FirstOrDefault(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)) ?? SystemTheme;
    }

    /// <summary>Applies the theme to <see cref="Application.Current"/> (a no-op without a running application).</summary>
    public void Apply(string? theme)
    {
        var name = Normalize(theme);
        CurrentTheme = name;

        if (Application.Current is not { } application)
        {
            return;
        }

        var variant = name switch
        {
            LightTheme => ThemeVariant.Light,
            DarkTheme => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Dispatcher.UIThread.CheckAccess())
        {
            application.RequestedThemeVariant = variant;
        }
        else
        {
            Dispatcher.UIThread.Post(() => application.RequestedThemeVariant = variant);
        }
    }
}
