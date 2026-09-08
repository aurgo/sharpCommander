using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using SharpCommander.Desktop.Localization;
using Xunit;

namespace SharpCommander.Tests;

public class LocalizationTests : IDisposable
{
    private readonly string _original = Strings.CurrentLanguage;

    public void Dispose() => Strings.Use(_original);

    [Fact]
    public void TheTwoCataloguesHaveExactlyTheSameKeys()
    {
        var english = Keys("English");
        var spanish = Keys("Spanish");

        // A missing key would silently render as the key itself; an extra one is dead weight from a rename.
        Assert.Empty(english.Except(spanish));
        Assert.Empty(spanish.Except(english));
        Assert.NotEmpty(english);
    }

    [Fact]
    public void SpanishActuallyTranslates()
    {
        Strings.Use("es");

        Assert.Equal("_Archivo", Strings.Get("MainWindow_File"));
        Assert.Equal("Tamaño", Strings.Get("FilePanelView_Size"));
        Assert.Equal("Abrir en una pestaña nueva", Strings.Get("FilePanelView_OpenInNewTab"));
    }

    [Fact]
    public void MissingKeyFallsBackToTheKeyItself()
    {
        Strings.Use("en");

        Assert.Equal("No_Such_Key", Strings.Get("No_Such_Key"));
    }

    [Fact]
    public void UnknownLanguageFallsBackToEnglish()
    {
        Strings.Use("en");
        var english = Strings.Get("MainWindow_File");

        Strings.Use("kl");

        Assert.Equal(english, Strings.Get("MainWindow_File"));
    }

    [Fact]
    public void SwitchingLanguageRefreshesEveryLiveText()
    {
        Strings.Use("en");
        var text = Strings.TextFor("MainWindow_File");
        var notified = 0;
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(LocalizedText.Value))
            {
                notified++;
            }
        };

        text.PropertyChanged += handler;
        try
        {
            Strings.Use("es");

            Assert.Equal(1, notified);
            Assert.Equal("_Archivo", text.Value);
        }
        finally
        {
            text.PropertyChanged -= handler;
        }
    }

    [Fact]
    public void SystemFollowsTheOperatingSystemAndIsRemembered()
    {
        Strings.Use("System");

        Assert.Equal("System", Strings.CurrentLanguage);
    }

    /// <summary>The keys of one catalogue. Internal types, so reflection is the only way in from a test.</summary>
    private static HashSet<string> Keys(string catalogue)
    {
        var field = typeof(Strings).Assembly.GetType($"SharpCommander.Desktop.Localization.{catalogue}")!
            .GetField("Texts")!;
        var texts = (System.Collections.IDictionary)field.GetValue(null)!;
        return texts.Keys.Cast<string>().ToHashSet(StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public void ABoundControlFollowsTheLanguage()
    {
        Strings.Use("en");
        var block = new TextBlock();

        // Exactly what the XAML does: {loc:T MainWindow_File}.
        block.Bind(TextBlock.TextProperty, (IBinding)new TExtension("MainWindow_File").ProvideValue(null!));
        Assert.Equal("_File", block.Text);

        Strings.Use("es");

        // An indexer binding would still read "_File" here; this is why the texts are bound as a property.
        Assert.Equal("_Archivo", block.Text);
    }

    [Fact]
    public void TheSameKeyAlwaysHandsBackTheSameObject()
    {
        Assert.Same(Strings.TextFor("MainWindow_File"), Strings.TextFor("MainWindow_File"));
    }

    [Fact]
    public void TheBoundPropertyIsRootedAgainstTrimming()
    {
        // The binding reaches LocalizedText.Value by reflection, so nothing in the code references it: the
        // published build trims the getter away and every text renders empty, which no other test can see
        // because tests run untrimmed. This guards the attribute that keeps it.
        var provideValue = typeof(TExtension).GetMethod(nameof(TExtension.ProvideValue))!;
        var dependency = provideValue
            .GetCustomAttributes(typeof(System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute), false)
            .Cast<System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute>()
            .SingleOrDefault();

        Assert.NotNull(dependency);
        Assert.Equal(typeof(LocalizedText), dependency.Type);
        Assert.True(dependency.MemberTypes.HasFlag(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties));
    }
}
