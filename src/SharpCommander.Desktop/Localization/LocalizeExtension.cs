using System.Diagnostics.CodeAnalysis;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace SharpCommander.Desktop.Localization;

/// <summary>
/// Resolves a text in XAML: <c>Header="{loc:T Menu_File}"</c>. It produces a binding rather than a plain string,
/// so switching language updates the interface in place instead of needing a restart.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    /// <summary>The key to look up.</summary>
    public string Key { get; set; } = key;

    // The binding finds LocalizedText.Value by reflection, and nameof() does not count as a reference: with
    // PublishTrimmed the getter is removed and every text renders empty. This roots it explicitly.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LocalizedText))]
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // A property binding, not an indexer one: Avalonia refreshes the former on PropertyChanged and does
        // not reliably refresh the latter, which would leave the interface in the old language after a switch.
        return new Binding(nameof(LocalizedText.Value))
        {
            Mode = BindingMode.OneWay,
            Source = Strings.TextFor(Key)
        };
    }
}
