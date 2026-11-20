using System.Linq;
using ObtainiumPC.Services.Localization;
using Xunit;

namespace ObtainiumPC.Tests;

/// LocaleResolution + the generated LocTranslations table (upstream
/// assets/translations reuse, lib/main.dart supportedLocales).
public class LocaleResolutionTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("en", "en")]
    [InlineData("ar", "ar")]
    [InlineData("ar-EG", "ar")]      // region variant falls back to language
    [InlineData("AR-sa", "ar")]
    [InlineData("fr-FR", "en")]      // languages that are not offered fall back to English
    [InlineData("pt-BR", "en")]
    [InlineData("zh-Hant-TW", "en")]
    [InlineData("zh-CN", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("fa-IR", "en")]
    [InlineData("xx", "en")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    public void Resolve_maps_system_tags(string? tag, string expected) =>
        Assert.Equal(expected, LocaleResolution.Resolve(tag));

    [Fact]
    public void Offered_languages_are_English_and_Arabic_only()
    {
        Assert.Equal(new[] { "en", "ar" }, LocaleResolution.OfferedLanguages.Select(l => l.Tag).ToArray());
        Assert.Equal("English", LocaleResolution.OfferedLanguages[0].Name);
        Assert.Equal("\u0627\u0644\u0639\u0631\u0628\u064a\u0629", LocaleResolution.OfferedLanguages[1].Name);
    }

    [Theory]
    [InlineData("ar", "en", "ar")]   // an offered choice wins over the system language
    [InlineData("en", "ar", "en")]
    [InlineData("", "ar", "ar")]     // follow system
    [InlineData(null, "en", "en")]
    [InlineData("de", "en", "en")]   // saved by an earlier version, no longer offered: follow system
    [InlineData("fr", "ar", "ar")]
    [InlineData("zh-Hant-TW", "en", "en")]
    public void Stored_language_that_is_no_longer_offered_follows_the_system(string? stored, string system, string expected) =>
        Assert.Equal(expected, LocaleResolution.ResolveStored(stored, () => system));

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

public class LocaleResolutionChineseTests
{
    [Theory]
    [InlineData("zh-HK")]
    [InlineData("zh-MO")]
    [InlineData("zh-Hans-CN")]
    public void Chinese_variants_are_not_offered_and_resolve_to_English(string tag) =>
        Assert.Equal("en", LocaleResolution.Resolve(tag));
}
