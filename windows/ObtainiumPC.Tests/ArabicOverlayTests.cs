using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ObtainiumPC.Tests;

/// Loc.cs is UI-bound and not compiled into the test project, so this reads its source:
/// every message the pages now pass through Loc.T has an Arabic entry.
public class ArabicOverlayTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ObtainiumPC", "Services", "Localization", "Loc.cs"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void Every_literal_status_message_passed_to_Loc_T_has_an_Arabic_entry()
    {
        var root = Root();
        var loc = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Localization", "Loc.cs"));
        var upstream = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Localization", "LocTranslations.g.cs"));
        var re = new Regex("(?:Show|ShowResult)\\(InfoBarSeverity\\.[A-Za-z]+, Services\\.Localization\\.Loc\\.T\\(\"([^\"]*)\"\\)");
        var keys = Directory.GetFiles(Path.Combine(root, "ObtainiumPC", "Pages"), "*.cs")
            .SelectMany(f => re.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.True(keys.Count >= 10);
        foreach (var k in keys)
            Assert.True(loc.Contains("[\"" + k + "\"] = \""), "no Arabic text for: " + k);
    }
}
