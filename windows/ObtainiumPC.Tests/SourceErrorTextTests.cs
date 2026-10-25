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
    public void Upstream_keyed_errors_are_localized_and_keep_their_details()
    {
        Assert.Equal(Ar("Could not determine release version"),
            SourceErrorText.Describe(new ObtainiumPC.Sources.FDroid.NoVersionException(), Ar));
        Assert.Equal(Ar("No APK found"), SourceErrorText.Describe(new NoAppAssetException(), Ar));
        var moved = SourceErrorText.Describe(new RepositoryRenamedException("https://a/x", "https://a/y"), Ar);
        Assert.Contains("https://a/x -> https://a/y", moved);
        Assert.StartsWith(Ar("Repository moved"), moved);
    }

    [Fact]
    public void Http_and_invalid_url_errors_show_the_status_or_source_without_guessing_a_cause()
    {
        Assert.Equal("[403]", SourceErrorText.Describe(new ObtainiumHttpException(403, "https://x"), en => "[{0}]"));
        Assert.Equal("src=GitHub", SourceErrorText.Describe(new InvalidUrlException("GitHub"), en => "src={0}"));
    }

    [Fact]
    public void Other_exceptions_keep_their_original_message()
    {
        Assert.Equal("boom", SourceErrorText.Describe(new System.InvalidOperationException("boom"), Ar));
        Assert.Equal("No installable asset found for this app", SourceErrorText.Describe(new NoAppAssetException(), en => en));
        Assert.Equal("Releases", SourceErrorText.Describe(new System.Exception("Releases"), Ar));
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
