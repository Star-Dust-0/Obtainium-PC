using ObtainiumPC.Services.Localization;
using Xunit;

namespace ObtainiumPC.Tests;

/// PluralRules: CLDR cardinal categories for the shipped locales (the rule
/// set easy_localization applies upstream).
public class PluralRulesTests
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "one")]
    [InlineData(2, "two")]
    [InlineData(3, "few")]
    [InlineData(10, "few")]
    [InlineData(11, "many")]
    [InlineData(99, "many")]
    [InlineData(100, "other")]
    [InlineData(102, "other")]
    [InlineData(103, "few")]
    [InlineData(111, "many")]
    public void Arabic_full_category_ladder(int n, string expected) =>
        Assert.Equal(expected, PluralRules.Select("ar", n));

    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(4, "few")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(21, "one")]
    [InlineData(22, "few")]
    [InlineData(25, "many")]
    [InlineData(111, "many")]
    public void Russian_mod_rules(int n, string expected) =>
        Assert.Equal(expected, PluralRules.Select("ru", n));

    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(5, "many")]
    [InlineData(12, "many")]
    public void Polish_rules(int n, string expected) =>
        Assert.Equal(expected, PluralRules.Select("pl", n));

    [Theory]
    [InlineData(1, "one")]
    [InlineData(3, "few")]
    [InlineData(5, "other")]
    public void Czech_rules(int n, string expected) =>
        Assert.Equal(expected, PluralRules.Select("cs", n));

    [Theory]
    [InlineData("fr", 0, "one")]   // French counts 0 as singular
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("pt-BR", 0, "one")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 0, "other")]
    [InlineData("en", 2, "other")]
    [InlineData("ja", 1, "other")] // no plural distinction
    [InlineData("zh-Hant-TW", 1, "other")]
    [InlineData("ko", 5, "other")]
    public void Other_languages(string tag, int n, string expected) =>
        Assert.Equal(expected, PluralRules.Select(tag, n));
}

/// LocPlurals.g.cs + Loc.Plural: generated per-language form tables keyed by
/// the English "other" literal, with easy_localization-style form fallback.
public class LocPluralTableTests
{
    [Fact]
    public void English_forms_present_for_all_plural_keys()
    {
        Assert.True(LocPlurals.ByLang.ContainsKey("en"));
        var en = LocPlurals.ByLang["en"];
        Assert.True(en.Count >= 20);
        Assert.True(en.TryGetValue("{} Apps", out var apps));
        Assert.Equal("{} App", apps["one"]);
        Assert.Equal("{} Apps", apps["other"]);
    }

    [Fact]
    public void Arabic_data_carries_one_and_other()
    {
        var ar = LocPlurals.ByLang["ar"];
        Assert.True(ar.TryGetValue("{} Apps", out var apps));
        Assert.Equal("{} تطبيق", apps["one"]);
        Assert.Equal("{} تطبيقات", apps["other"]);
    }

    [Theory]
    // ar.json ships only one/other; missing categories fall back to other
    // (easy_localization behavior), so the full CLDR ladder resolves to
    // exactly the forms upstream would render.
    [InlineData(0, "{} تطبيقات")] // zero -> other
    [InlineData(1, "{} تطبيق")]   // one
    [InlineData(2, "{} تطبيقات")] // two -> other
    [InlineData(3, "{} تطبيقات")] // few -> other
    [InlineData(11, "{} تطبيقات")]// many -> other
    [InlineData(100, "{} تطبيقات")]
    public void Arabic_form_selection_with_fallback(int n, string expected)
    {
        var forms = LocPlurals.ByLang["ar"]["{} Apps"];
        Assert.Equal(expected, PluralRules.SelectForm(forms, "ar", n, "{} Apps"));
    }

    [Theory]
    [InlineData(1, "{} приложение")]  // one
    [InlineData(3, "{} приложения")]  // few
    [InlineData(5, "{} приложений")]  // many missing in ru.json -> other
    [InlineData(21, "{} приложение")]
    public void Russian_form_selection_with_fallback(int n, string expected)
    {
        var forms = LocPlurals.ByLang["ru"]["{} Apps"];
        Assert.Equal(expected, PluralRules.SelectForm(forms, "ru", n, "{} Apps"));
    }

    [Theory]
    [InlineData(1, "{} App")]
    [InlineData(2, "{} Apps")]
    public void English_form_selection(int n, string expected)
    {
        var forms = LocPlurals.ByLang["en"]["{} Apps"];
        Assert.Equal(expected, PluralRules.SelectForm(forms, "en", n, "{} Apps"));
    }

    [Fact]
    public void Russian_data_carries_few()
    {
        var ru = LocPlurals.ByLang["ru"];
        Assert.True(ru.TryGetValue("{} Apps", out var apps));
        Assert.True(apps.ContainsKey("few"));
    }
}
