using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace ObtainiumPC.Services;

/// <summary>
/// Minimal markdown renderer for changelogs (upstream renders full markdown
/// via flutter_markdown_plus). Covers the common changelog subset: headings,
/// bold/italic, inline code, links, bullet and numbered lists. Anything else
/// falls through as plain text. Disclosed in PARITY.md.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex InlineRegex = new(
        @"(\[([^\]]+)\]\(([^)]+)\))|(\*\*([^*]+)\*\*)|(\*([^*]+)\*)|(`([^`]+)`)",
        RegexOptions.Compiled);

    public static RichTextBlock Render(string markdown)
    {
        var block = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        Paragraph? currentList = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                currentList = null;
                continue;
            }

            if (line.StartsWith("### "))
            {
                currentList = null;
                block.Blocks.Add(Heading(line[4..], 16));
                continue;
            }
            if (line.StartsWith("## "))
            {
                currentList = null;
                block.Blocks.Add(Heading(line[3..], 18));
                continue;
            }
            if (line.StartsWith("# "))
            {
                currentList = null;
                block.Blocks.Add(Heading(line[2..], 22));
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var p = new Paragraph { Margin = new Thickness(indent + 12, 1, 0, 1) };
                p.Inlines.Add(new Run { Text = "•  " });
                AddInlines(p, trimmed[2..]);
                block.Blocks.Add(p);
                currentList = p;
                continue;
            }
            var numbered = Regex.Match(trimmed, @"^\d+[.)]\s+");
            if (numbered.Success)
            {
                var p = new Paragraph { Margin = new Thickness(indent + 12, 1, 0, 1) };
                p.Inlines.Add(new Run { Text = numbered.Value });
                AddInlines(p, trimmed[numbered.Length..]);
                block.Blocks.Add(p);
                currentList = p;
                continue;
            }

            var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
            AddInlines(para, line);
            block.Blocks.Add(para);
            currentList = null;
        }
        return block;
    }

    private static Paragraph Heading(string text, double size)
    {
        var p = new Paragraph { Margin = new Thickness(0, 8, 0, 2) };
        p.Inlines.Add(new Run { Text = text, FontWeight = FontWeights.SemiBold, FontSize = size });
        return p;
    }

    private static void AddInlines(Paragraph p, string text)
    {
        var pos = 0;
        foreach (Match m in InlineRegex.Matches(text))
        {
            if (m.Index > pos)
                p.Inlines.Add(new Run { Text = text[pos..m.Index] });
            if (m.Groups[1].Success && Uri.TryCreate(m.Groups[3].Value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var link = new Hyperlink { NavigateUri = uri };
                link.Inlines.Add(new Run { Text = m.Groups[2].Value });
                p.Inlines.Add(link);
            }
            else if (m.Groups[4].Success)
            {
                p.Inlines.Add(new Run { Text = m.Groups[5].Value, FontWeight = FontWeights.SemiBold });
            }
            else if (m.Groups[6].Success)
            {
                p.Inlines.Add(new Run { Text = m.Groups[7].Value, FontStyle = FontStyle.Italic });
            }
            else if (m.Groups[8].Success)
            {
                p.Inlines.Add(new Run
                {
                    Text = m.Groups[9].Value,
                    FontFamily = new FontFamily("Consolas"),
                });
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            p.Inlines.Add(new Run { Text = text[pos..] });
        if (p.Inlines.Count == 0)
            p.Inlines.Add(new Run { Text = "" });
    }
}
