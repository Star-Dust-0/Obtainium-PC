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
    public void Every_literal_passed_to_Loc_T_in_the_pages_has_a_translation_source()
    {
        var root = Root();
        var loc = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Localization", "Loc.cs"));
        var upstream = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Localization", "LocTranslations.g.cs"));
        var re = new Regex("Services\\.Localization\\.Loc\\.T\\(\"([^\"]*)\"\\)");
        var keys = Directory.GetFiles(Path.Combine(root, "ObtainiumPC", "Pages"), "*.cs")
            .SelectMany(f => re.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.True(keys.Count >= 10);
        foreach (var k in keys)
        {
            var up = k.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("{}", "\\u007b\\u007d").Replace("{0}", "\\u007b0\\u007d");
            Assert.True(loc.Contains("[\"" + k + "\"] = \"") || upstream.Contains("[\"" + up + "\"] = \""), "no Arabic text for: " + k);
        }
    }

    [Fact]
    public void Every_literal_passed_to_translate_in_AppsSummaryText_has_a_translation_source()
    {
        // The summary literals live in Services (passed as the translate delegate), outside the
        // Pages scan above; this is the gap that once shipped English-only summary states.
        var root = Root();
        var loc = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Localization", "Loc.cs"));
        var summary = File.ReadAllText(Path.Combine(root, "ObtainiumPC", "Services", "Updates", "AppsSummaryText.cs"));
        var re = new Regex("translate\\(\"([^\"]*)\"\\)");
        var keys = re.Matches(summary).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(keys.Count >= 5);
        foreach (var k in keys)
            Assert.True(loc.Contains("[\"" + k + "\"] = \""), "no Arabic text for: " + k);
    }
}
