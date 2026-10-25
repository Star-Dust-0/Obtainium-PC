using ObtainiumPC.Services.Localization;
using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

public class SourceErrorTextTests
{
    private static string Ar(string en) =>
        LocTranslations.ByLang["ar"].TryGetValue(en, out var v) ? v : en;

    [Fact]
    public void NoReleases_is_localized_with_the_upstream_key_in_arabic()
    {
        var text = SourceErrorText.Describe(new NoReleasesException(), Ar);
        Assert.Equal("تعذر العثور على إصدار مناسب", text);
        Assert.NotEqual("No releases found", text);
    }

    [Fact]
    public void NoWindowsInstaller_uses_the_distinct_key_and_english_passes_through()
    {
        var ex = NoAppAssetException.NoWindowsInstaller();
        Assert.Equal(NoAppAssetException.NoWindowsInstallerMessage, SourceErrorText.Describe(ex, en => en));
        Assert.Equal("KEY:" + NoAppAssetException.NoWindowsInstallerMessage, SourceErrorText.Describe(ex, en => "KEY:" + en));
    }

    [Fact]
    public void Other_exceptions_keep_their_original_message()
    {
        Assert.Equal("boom", SourceErrorText.Describe(new System.InvalidOperationException("boom"), Ar));
        Assert.Equal("No installable asset found for this app", SourceErrorText.Describe(new NoAppAssetException(), en => en));
    }

    [Fact]
    public void Arabic_overlay_in_Loc_has_a_translation_for_the_distinct_message()
    {
        // Loc.cs needs Windows APIs, so read its ArOverlay source: the key must be
        // present with a non-ASCII (Arabic) value, not just the English literal.
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ObtainiumPC", "Services", "Localization", "Loc.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "ObtainiumPC", "Services", "Localization", "Loc.cs"));
        var i = src.IndexOf("Releases were found, but none has a Windows installer asset", System.StringComparison.Ordinal);
        Assert.True(i > 0, "ArOverlay key missing");
        var tail = src.Substring(i, 700);
        Assert.Matches("[\\u0600-\\u06FF]{3,}", tail);
    }
}
