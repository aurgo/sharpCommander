using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;

namespace SharpCommander.Desktop.Localization;

/// <summary>
/// The translated texts, as plain dictionaries rather than .resx satellite assemblies. Resource managers reach
/// for their assemblies by reflection, which fights both trimming and AOT; a dictionary is looked up the same way
/// on every platform and survives whatever the publish settings do.
///
/// XAML binds through <see cref="TextFor"/>, which hands out one <see cref="LocalizedText"/> per key and keeps
/// it: a plain property binding refreshes reliably where an indexer binding does not, and because the objects
/// are cached there is exactly one per key however many controls use it, so nothing leaks.
/// </summary>
public sealed class Strings : INotifyPropertyChanged
{
    /// <summary>The languages on offer, by culture name.</summary>
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Catalogues =
        new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = English.Texts,
            ["es"] = Spanish.Texts
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private FrozenDictionary<string, string> _texts = English.Texts;

    private Strings()
    {
    }

    /// <summary>The single instance every binding and view model reads from.</summary>
    public static Strings Instance { get; } = new();

    /// <summary>The language in use, as a culture name ("en", "es").</summary>
    public static string CurrentLanguage { get; private set; } = "en";

    /// <summary>The languages that can be chosen, in the order they should be offered.</summary>
    public static IReadOnlyList<LanguageOption> Available { get; } =
    [
        new("System", "System default"),
        new("en", "English"),
        new("es", "Español")
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after every text has been refreshed. The views listen for it to redo their layout: a translated
    /// word is rarely the same width as the original, and the panels that hold them keep the size they were
    /// arranged with, so without this the new text is drawn clipped into the old slot.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// The text for a key, or the key itself when it is missing. Returning the key rather than throwing means a
    /// forgotten string shows up as an odd label instead of taking the window down.
    /// </summary>
    public string this[string key] => _texts.TryGetValue(key, out var text) ? text : key;

    /// <summary>Looks up a key without going through the instance, for code that formats its own messages.</summary>
    public static string Get(string key) => Instance[key];

    /// <summary>Formats a text that carries {0}-style placeholders.</summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Instance[key], arguments);

    /// <summary>
    /// Switches language. "System" follows the operating system, falling back to English when it speaks a
    /// language this application does not. Every bound text updates at once.
    /// </summary>
    public static void Use(string? language)
    {
        var wanted = string.IsNullOrWhiteSpace(language) || string.Equals(language, "System", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : language;

        var texts = Catalogues.TryGetValue(wanted, out var found) ? found : English.Texts;

        CurrentLanguage = string.IsNullOrWhiteSpace(language) ? "System" : language;
        Instance._texts = texts;

        foreach (var text in Texts.Values)
        {
            text.Refresh();
        }

        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(null));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The bindable text for a key. The same object is returned every time, so bindings are cheap.</summary>
    public static LocalizedText TextFor(string key) => Texts.GetOrAdd(key, static k => new LocalizedText(k));

    private static readonly ConcurrentDictionary<string, LocalizedText> Texts = new(StringComparer.Ordinal);
}

/// <summary>
/// One translated text, as a property a binding can follow. Created and cached by
/// <see cref="Strings.TextFor"/>; changing language refreshes every one of them at once.
/// </summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    internal LocalizedText(string key)
    {
        Key = key;
    }

    public string Key { get; }

    /// <summary>The text in the language currently in use.</summary>
    public string Value => Strings.Get(Key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

/// <summary>A language the user can pick, with the name to show for it.</summary>
public sealed record LanguageOption(string Code, string DisplayName);
