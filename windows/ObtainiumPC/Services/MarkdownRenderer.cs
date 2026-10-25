using System;
using System.Collections.Generic;
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

    private static readonly Regex TableSeparator = new(
        @"^\|?[\s:|-]+\|[\s:|-]*$", RegexOptions.Compiled);

    private static readonly Regex HtmlTag = new(@"<[^>]+>", RegexOptions.Compiled);

    private static string StripHtml(string text) =>
        HtmlTag.Replace(text.Replace("<br>", " ").Replace("<br/>", " ").Replace("<br />", " "), "");

    public static RichTextBlock Render(string markdown)
    {
        var block = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        // FootnoteDefSyntax (upstream's gitHubFlavored set): collect
        // [^id]: definition lines, render refs as superscript numbers and
        // the definitions as a numbered section at the end.
        var footnoteDefs = new List<(string Id, string Text)>();
        var footnoteIds = new HashSet<string>();
        var kept = new List<string>();
        foreach (var raw in lines)
        {
            var def = Regex.Match(raw, @"^\[\^(?<id>[^\]]+)\]:\s*(?<text>.*)$");
            if (def.Success)
            {
                var id = def.Groups["id"].Value;
                if (!footnoteIds.Contains(id))
                {
                    footnoteIds.Add(id);
                    footnoteDefs.Add((id, def.Groups["text"].Value));
                }
                continue;
            }
            kept.Add(raw);
        }
        lines = kept.ToArray();
        _footnoteNumbers = footnoteDefs.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i + 1);
        Paragraph? currentList = null;
        var inFence = false;
        Paragraph? fencePara = null;
        for (var li = 0; li < lines.Length; li++)
        {
            var line = lines[li].TrimEnd();

            // Fenced code blocks (```lang ... ```): monospace, preserved.
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
                if (inFence)
                {
                    fencePara = new Paragraph
                    {
                        Margin = new Thickness(8, 4, 0, 4),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12.5,
                    };
                    block.Blocks.Add(fencePara);
                }
                else fencePara = null;
                currentList = null;
                continue;
            }
            if (inFence)
            {
                fencePara!.Inlines.Add(new Run { Text = line + "\n" });
                continue;
            }

            if (line.Length == 0)
            {
                currentList = null;
                continue;
            }

            // Pipe tables: header row, |---| separator, body rows.
            if (line.Contains('|') && li + 1 < lines.Length && TableSeparator.IsMatch(lines[li + 1].Trim()))
            {
                var headerCells = SplitTableRow(line);
                var rows = new List<string[]>();
                var ri = li + 2;
                while (ri < lines.Length && lines[ri].Trim().Contains('|') && lines[ri].Trim().Length > 0)
                {
                    rows.Add(SplitTableRow(lines[ri]));
                    ri++;
                }
                block.Blocks.Add(BuildTable(headerCells, rows));
                li = ri - 1;
                currentList = null;
                continue;
            }

            // Horizontal rule.
            if (line.Trim() is "---" or "***" or "___")
            {
                var hr = new Paragraph { Margin = new Thickness(0, 8, 0, 8) };
                hr.Inlines.Add(new Run
                {
                    Text = new string('─', 48),
                    Foreground = Pages.ThemeLookup.Brush("TextFillColorSecondaryBrush") ?? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
                block.Blocks.Add(hr);
                currentList = null;
                continue;
            }

            // Blockquote: indented, secondary color.
            if (line.TrimStart().StartsWith("> "))
            {
                var qp = new Paragraph
                {
                    Margin = new Thickness(12, 2, 0, 2),
                    FontStyle = FontStyle.Italic,
                    Foreground = Pages.ThemeLookup.Brush("TextFillColorSecondaryBrush") ?? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
                };
                AddInlines(qp, StripHtml(line.TrimStart()[2..]));
                block.Blocks.Add(qp);
                currentList = null;
                continue;
            }

            if (line.StartsWith("### "))
            {
                currentList = null;
                block.Blocks.Add(Heading(StripHtml(line[4..]), 16));
                continue;
            }
            if (line.StartsWith("## "))
            {
                currentList = null;
                block.Blocks.Add(Heading(StripHtml(line[3..]), 18));
                continue;
            }
            if (line.StartsWith("# "))
            {
                currentList = null;
                block.Blocks.Add(Heading(StripHtml(line[2..]), 22));
                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var content = trimmed[2..];
                // GFM task-list items: - [ ] / - [x]
                var bullet = "•  ";
                if (content.StartsWith("[ ] ")) { bullet = "☐  "; content = content[4..]; }
                else if (content.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) { bullet = "☑  "; content = content[4..]; }
                var p = new Paragraph { Margin = new Thickness(indent + 12, 1, 0, 1) };
                p.Inlines.Add(new Run { Text = bullet });
                AddInlines(p, StripHtml(content));
                block.Blocks.Add(p);
                currentList = p;
                continue;
            }
            var numbered = Regex.Match(trimmed, @"^\d+[.)]\s+");
            if (numbered.Success)
            {
                var p = new Paragraph { Margin = new Thickness(indent + 12, 1, 0, 1) };
                p.Inlines.Add(new Run { Text = numbered.Value });
                AddInlines(p, StripHtml(trimmed[numbered.Length..]));
                block.Blocks.Add(p);
                currentList = p;
                continue;
            }

            // <summary> labels inside <details> blocks (common in release
            // notes): render the label bold; the details body falls through
            // as plain paragraphs, matching the plain-text fallback for raw
            // HTML.
            var summary = Regex.Match(line, @"<summary>(?<s>.*?)</summary>", RegexOptions.IgnoreCase);
            if (summary.Success)
            {
                var sp = new Paragraph { Margin = new Thickness(0, 6, 0, 2) };
                sp.Inlines.Add(new Run { Text = summary.Groups["s"].Value.Trim(), FontWeight = FontWeights.SemiBold });
                block.Blocks.Add(sp);
                currentList = null;
                continue;
            }
            var stripped = StripHtml(line).Trim();
            if (stripped.Length == 0) { currentList = null; continue; } // tag-only lines (<details> etc.)
            var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
            AddInlines(para, stripped);
            block.Blocks.Add(para);
            currentList = null;
        }
        if (footnoteDefs.Count > 0)
        {
            var hr = new Paragraph { Margin = new Thickness(0, 10, 0, 2) };
            hr.Inlines.Add(new Run
            {
                Text = new string('─', 24),
                Foreground = Pages.ThemeLookup.Brush("TextFillColorSecondaryBrush") ?? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            block.Blocks.Add(hr);
            var n = 0;
            foreach (var (id, text) in footnoteDefs)
            {
                n++;
                var fp = new Paragraph { Margin = new Thickness(0, 1, 0, 1), FontSize = 12 };
                fp.Inlines.Add(new Run { Text = n + ". ", FontWeight = FontWeights.SemiBold });
                AddInlines(fp, StripHtml(text));
                block.Blocks.Add(fp);
            }
        }
        return block;
    }

    private static Dictionary<string, int> _footnoteNumbers = new();

    private static string[] SplitTableRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("|")) t = t[1..];
        if (t.EndsWith("|")) t = t[..^1];
        var cells = t.Split('|');
        for (var i = 0; i < cells.Length; i++) cells[i] = StripHtml(cells[i].Trim());
        return cells;
    }

    private static Paragraph BuildTable(string[] header, List<string[]> rows)
    {
        var cols = header.Length;
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in rows)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Cell(string text, int row, int col, bool isHeader)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = Pages.ThemeLookup.Brush("DividerStrokeColorDefaultBrush") ?? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                Padding = new Thickness(8, 4, 8, 4),
            };
            var para = new Paragraph();
            AddInlines(para, text);
            var tb = new RichTextBlock { TextWrapping = TextWrapping.Wrap };
            if (isHeader)
                foreach (var inline in para.Inlines)
                    if (inline is Run r) r.FontWeight = FontWeights.SemiBold;
            tb.Blocks.Add(para);
            border.Child = tb;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }

        for (var c = 0; c < cols; c++) Cell(header[c], 0, c, true);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < cols && c < rows[r].Length; c++)
                Cell(rows[r][c], r + 1, c, false);

        return new Paragraph
        {
            Inlines = { new InlineUIContainer { Child = grid } },
        };
    }

    private static Paragraph Heading(string text, double size)
    {
        var p = new Paragraph { Margin = new Thickness(0, 8, 0, 2) };
        p.Inlines.Add(new Run { Text = text, FontWeight = FontWeights.SemiBold, FontSize = size });
        return p;
    }

    private static void AddInlines(Paragraph p, string text)
    {
        foreach (var seg in MarkdownInlineParser.Parse(text))
        {
            switch (seg.Kind)
            {
                case MarkdownInlineKind.Image:
                {
                    var image = new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(seg.Url!)),
                        MaxWidth = 460,
                        MaxHeight = 320,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 4, 0, 4),
                    };
                    if (seg.Text.Length > 0) ToolTipService.SetToolTip(image, seg.Text);
                    p.Inlines.Add(new InlineUIContainer { Child = image });
                    break;
                }
                case MarkdownInlineKind.Link:
                {
                    var link = new Hyperlink { NavigateUri = new Uri(seg.Url!) };
                    link.Inlines.Add(new Run { Text = seg.Text });
                    p.Inlines.Add(link);
                    break;
                }
                case MarkdownInlineKind.Bold:
                    p.Inlines.Add(new Run { Text = seg.Text, FontWeight = FontWeights.SemiBold });
                    break;
                case MarkdownInlineKind.Italic:
                    p.Inlines.Add(new Run { Text = seg.Text, FontStyle = FontStyle.Italic });
                    break;
                case MarkdownInlineKind.BoldItalic:
                    p.Inlines.Add(new Run { Text = seg.Text, FontWeight = FontWeights.SemiBold, FontStyle = FontStyle.Italic });
                    break;
                case MarkdownInlineKind.Strikethrough:
                    p.Inlines.Add(new Run
                    {
                        Text = seg.Text,
                        TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough,
                    });
                    break;
                case MarkdownInlineKind.Code:
                    p.Inlines.Add(new Run { Text = seg.Text, FontFamily = new FontFamily("Consolas") });
                    break;
                case MarkdownInlineKind.FootnoteRef:
                {
                    var num = _footnoteNumbers.TryGetValue(seg.Text, out var fn) ? fn.ToString() : seg.Text;
                    p.Inlines.Add(new Run
                    {
                        Text = "[" + num + "]",
                        FontSize = (p.FontSize > 0 ? p.FontSize : 14) * 0.75,
                        Foreground = Pages.ThemeLookup.Brush("AccentTextFillColorPrimaryBrush") ?? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                    });
                    break;
                }
                default:
                    p.Inlines.Add(new Run { Text = seg.Text });
                    break;
            }
        }
        if (p.Inlines.Count == 0)
            p.Inlines.Add(new Run { Text = "" });
    }

}
