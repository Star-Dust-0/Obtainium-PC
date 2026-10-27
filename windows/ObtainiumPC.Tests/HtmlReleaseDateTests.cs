using ObtainiumPC.Sources.Html;
using Xunit;

namespace ObtainiumPC.Tests;

/// Port of test/html_release_date_test.dart (text-level cases).
public class HtmlReleaseDateTests
{
    [Fact]
    public void ParsesIsoDateCapturedByRegex() =>
        Assert.Equal(new DateTime(2026, 9, 3), HtmlSource.ParseHtmlReleaseDateText(
            @"Last modified: (\S+)", "$1", null, "App 1.0\nLast modified: 2026-09-03\n")!.Value.DateTime);

    [Fact]
    public void ParsesCustomFormatLikeMiXplorerBetaPage() =>
        Assert.Equal(new DateTime(2026, 9, 3, 23, 41, 0), HtmlSource.ParseHtmlReleaseDateText(
            @"Last modified: (.*)", "$1", "EEE. MMMM d, yyyy - h:mma",
            "MiXplorer_v6.71.15-BETA_B26090414-x64.apk\nSize: 5.17mbLast modified: Thu. September 3rd, 2026 - 11:41pm\n")!.Value.DateTime);

    [Fact]
    public void UsesWholeMatchWhenNoMatchGroup() =>
        Assert.Equal(new DateTime(2026, 1, 31), HtmlSource.ParseHtmlReleaseDateText(
            @"\d{4}-\d{2}-\d{2}", null, null, "Published 2026-01-31 by someone")!.Value.DateTime);

    [Fact]
    public void NullWhenRegexDoesNotMatch() =>
        Assert.Null(HtmlSource.ParseHtmlReleaseDateText(@"Last modified: (.*)", null, null, "No date"));

    [Fact]
    public void NullWhenCapturedTextIsNotADate() =>
        Assert.Null(HtmlSource.ParseHtmlReleaseDateText(@"Last modified: (.*)", "$1", null, "Last modified: yesterday"));

    [Fact]
    public void NullForInvalidDateFormatPattern() =>
        Assert.Null(HtmlSource.ParseHtmlReleaseDateText(
            @"Last modified: (.*)", "$1", "not a valid pattern: !!!", "Last modified: Thu. September 3rd, 2026 - 11:41pm"));

    [Fact]
    public void NullWhenNoRegexConfigured()
    {
        Assert.Null(HtmlSource.ParseHtmlReleaseDateText(null, null, null, "anything"));
        Assert.Null(HtmlSource.ParseHtmlReleaseDateText("", null, null, "anything"));
    }
}
