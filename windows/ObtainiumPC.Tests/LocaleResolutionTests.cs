using ObtainiumPC.Services.Localization;
using Xunit;

namespace ObtainiumPC.Tests;

/// LocaleResolution + the generated LocTranslations table (upstream
/// assets/translations reuse, lib/main.dart supportedLocales).
public class LocaleResolutionTests
{
    [Theory]
    [InlineData("fr-FR", "fr")]       // region variant falls back to language
    [InlineData("pt-BR", "pt-BR")]    // exact tag wins over language prefix
    [InlineData("zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("zh-TW", "zh-Hant-TW")] // traditional Chinese region maps to the Hant table
    [InlineData("zh-CN", "zh")]
    [InlineData("ar-EG", "ar")]
    [InlineData("de-DE", "de")]
    [InlineData("xx", "en")]          // unsupported language falls back to English
    [InlineData(null, "en")]
    [InlineData("", "en")]
    public void Resolve_maps_system_tags(string? tag, string expected) =>
        Assert.Equal(expected, LocaleResolution.Resolve(tag));

    [Fact]
    public void Supported_languages_cover_all_upstream_locales()
    {
        // lib/main.dart supportedLocales: 30 entries.
        Assert.Equal(30, LocTranslations.SupportedLanguages.Length);
        Assert.Contains(LocTranslations.SupportedLanguages, l => l.Tag == "ar");
        Assert.Contains(LocTranslations.SupportedLanguages, l => l.Tag == "en-EO");
    }

    [Fact]
    public void Generated_tables_carry_upstream_translations_keyed_by_english()
    {
        // ar.json "apps" plural-other is "تطبيقات"; de.json "settings" etc.
        Assert.True(LocTranslations.ByLang["ar"].TryGetValue("Apps", out var ar) && ar.Length > 0);
        Assert.NotEqual("Apps", ar);
        Assert.True(LocTranslations.ByLang["de"].Count > 100);
        Assert.False(LocTranslations.ByLang.ContainsKey("en"));
    }
}
