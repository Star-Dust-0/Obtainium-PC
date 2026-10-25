using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ObtainiumPC.Services;

public enum MarkdownInlineKind { Text, Bold, Italic, BoldItalic, Strikethrough, Code, Link, Image, FootnoteRef }

public readonly record struct MarkdownSegment(MarkdownInlineKind Kind, string Text, string? Url);

/// <summary>
/// Pure inline-markdown tokenizer for changelog rendering (upstream renders
/// via flutter_markdown_plus; this covers the changelog-relevant subset).
/// WinUI-free so the test project can link and unit-test it; MarkdownRenderer
/// turns segments into RichTextBlock inlines.
/// </summary>
public static class MarkdownInlineParser
{
    // Alternation order matters: img/link first so their URLs are consumed
    // before the bare-URL alternative can see them; bold-italic before bold
    // before italic for the same reason.
    private static readonly Regex InlineRegex = new(
        @"(?<img>!\[(?<imgalt>[^\]]*)\]\((?<imgurl>[^)\s]+)\))"
        + @"|(?<fnref>\[\^(?<fnid>[^\]]+)\])"
        + @"|(?<link>\[(?<linktext>[^\]]+)\]\((?<linkurl>[^)\s]+)\))"
        + @"|\*\*\*(?<bitext>[^*]+)\*\*\*|(?<![\w])___(?<bitext2>[^_]+)___(?![\w])"
        + @"|\*\*(?<boldtext>[^*]+)\*\*|(?<![\w])__(?<boldtext2>[^_]+)__(?![\w])"
        + @"|\*(?<italictext>[^*]+)\*|(?<![\w])_(?<italictext2>[^_]+)_(?![\w])"
        + @"|~~(?<striketext>[^~]+)~~"
        + @"|`(?<codetext>[^`]+)`"
        + @"|:(?<emoji>[a-z0-9_+-]+):"
        + @"|(?<bare>https?://[^\s<>\)\]]+)",
        RegexOptions.Compiled);

    private static readonly char[] BareTrailing = { '.', ',', ';', ':', '!', '?', '\'', '"' };

    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public static IReadOnlyList<MarkdownSegment> Parse(string text)
    {
        var segments = new List<MarkdownSegment>();
        var pos = 0;
        void Text(string s)
        {
            if (s.Length == 0) return;
            if (segments.Count > 0 && segments[^1].Kind == MarkdownInlineKind.Text)
                segments[^1] = new MarkdownSegment(MarkdownInlineKind.Text, segments[^1].Text + s, null);
            else
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Text, s, null));
        }
        foreach (Match m in InlineRegex.Matches(text))
        {
            if (m.Index < pos) continue; // overlap guard
            if (m.Index > pos) Text(text[pos..m.Index]);
            if (m.Groups["fnref"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.FootnoteRef, m.Groups["fnid"].Value, null));
            else if (m.Groups["emoji"].Success
                && EmojiTable.Emojis.TryGetValue(m.Groups["emoji"].Value, out var emoji))
                Text(emoji);
            else if (m.Groups["emoji"].Success)
                Text(m.Value); // unknown shortcode: keep raw text like upstream
            else if (m.Groups["img"].Success && IsWebUrl(m.Groups["imgurl"].Value))
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Image, m.Groups["imgalt"].Value, m.Groups["imgurl"].Value));
            else if (m.Groups["link"].Success && IsWebUrl(m.Groups["linkurl"].Value))
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Link, m.Groups["linktext"].Value, m.Groups["linkurl"].Value));
            else if (m.Groups["bitext"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.BoldItalic, m.Groups["bitext"].Value, null));
            else if (m.Groups["bitext2"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.BoldItalic, m.Groups["bitext2"].Value, null));
            else if (m.Groups["boldtext"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Bold, m.Groups["boldtext"].Value, null));
            else if (m.Groups["boldtext2"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Bold, m.Groups["boldtext2"].Value, null));
            else if (m.Groups["italictext"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Italic, m.Groups["italictext"].Value, null));
            else if (m.Groups["italictext2"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Italic, m.Groups["italictext2"].Value, null));
            else if (m.Groups["striketext"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Strikethrough, m.Groups["striketext"].Value, null));
            else if (m.Groups["codetext"].Success)
                segments.Add(new MarkdownSegment(MarkdownInlineKind.Code, m.Groups["codetext"].Value, null));
            else if (m.Groups["bare"].Success)
            {
                var url = m.Groups["bare"].Value.TrimEnd(BareTrailing);
                if (IsWebUrl(url))
                    segments.Add(new MarkdownSegment(MarkdownInlineKind.Link, url, url));
                else
                    Text(m.Value);
            }
            else Text(m.Value); // e.g. a link/image with a non-web URL: keep the raw text
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) Text(text[pos..]);
        return segments;
    }
}
