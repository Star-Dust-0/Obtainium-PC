using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class AppsSummaryTextTests
{
    private static string Id(string s) => s;

    [Fact]
    public void No_apps_is_empty()
    {
        Assert.Equal("", AppsSummaryText.Format(0, 0, 0, Id));
    }

    [Fact]
    public void All_current_claims_all_up_to_date()
    {
        Assert.Equal("3 apps, all up to date", AppsSummaryText.Format(3, 0, 0, Id));
    }

    [Fact]
    public void Updates_only()
    {
        Assert.Equal("3 apps, 1 with updates", AppsSummaryText.Format(3, 1, 0, Id));
    }

    [Fact]
    public void Failed_checks_are_named_not_counted_as_current()
    {
        Assert.Equal("2 apps, 1 with failed checks", AppsSummaryText.Format(2, 0, 1, Id));
    }

    [Fact]
    public void Updates_and_failed_checks_are_both_named()
    {
        Assert.Equal("4 apps, 1 with updates, 2 with failed checks", AppsSummaryText.Format(4, 1, 2, Id));
    }

    [Fact]
    public void Translate_is_used_for_every_variant()
    {
        var seen = new List<string>();
        string T(string s) { seen.Add(s); return s; }
        AppsSummaryText.Format(1, 1, 1, T);
        AppsSummaryText.Format(1, 1, 0, T);
        AppsSummaryText.Format(1, 0, 1, T);
        AppsSummaryText.Format(1, 0, 0, T);
        Assert.Equal(new[]
        {
            "{0} apps, {1} with updates, {2} with failed checks",
            "{0} apps, {1} with updates",
            "{0} apps, {1} with failed checks",
            "{0} apps, all up to date",
        }, seen);
    }
}
