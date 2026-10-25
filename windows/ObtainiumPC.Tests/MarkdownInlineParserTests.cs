using ObtainiumPC.Services;
using Xunit;

namespace ObtainiumPC.Tests;

/// MarkdownInlineParser: the pure tokenizer behind changelog rendering
/// (upstream renders full markdown via flutter_markdown_plus).
public class MarkdownInlineParserTests
{
    [Fact]
    public void Plain_text_passes_through()
    {
        var segs = MarkdownInlineParser.Parse("just words");
        Assert.Single(segs);
        Assert.Equal(MarkdownInlineKind.Text, segs[0].Kind);
        Assert.Equal("just words", segs[0].Text);
    }

    [Fact]
    public void Bold_italic_strike_code_and_mixed_text()
    {
        var segs = MarkdownInlineParser.Parse("a **b** _i_ ~~s~~ `c` z");
        Assert.Equal(new[]
        {
            MarkdownInlineKind.Text, MarkdownInlineKind.Bold, MarkdownInlineKind.Text,
            MarkdownInlineKind.Italic, MarkdownInlineKind.Text, MarkdownInlineKind.Strikethrough,
            MarkdownInlineKind.Text, MarkdownInlineKind.Code, MarkdownInlineKind.Text,
        }, segs.Select(s => s.Kind).ToArray());
        Assert.Equal("b", segs[1].Text);
        Assert.Equal("i", segs[3].Text);
        Assert.Equal("s", segs[5].Text);
        Assert.Equal("c", segs[7].Text);
    }

    [Fact]
    public void Bold_italic_combined_via_triple_stars()
    {
        var segs = MarkdownInlineParser.Parse("***bi***");
        Assert.Single(segs);
        Assert.Equal(MarkdownInlineKind.BoldItalic, segs[0].Kind);
        Assert.Equal("bi", segs[0].Text);
    }

    [Fact]
    public void Link_and_image_capture_url()
    {
        var segs = MarkdownInlineParser.Parse("[rel](https://x.com/a) ![alt](https://x.com/i.png)");
        Assert.Equal(MarkdownInlineKind.Link, segs[0].Kind);
        Assert.Equal("rel", segs[0].Text);
        Assert.Equal("https://x.com/a", segs[0].Url);
        Assert.Equal(MarkdownInlineKind.Image, segs[2].Kind);
        Assert.Equal("alt", segs[2].Text);
        Assert.Equal("https://x.com/i.png", segs[2].Url);
    }

    [Fact]
    public void Bare_url_linkified_and_trailing_punctuation_excluded()
    {
        var segs = MarkdownInlineParser.Parse("see https://example.com/changes.");
        Assert.Equal(2, segs.Count);
        Assert.Equal(MarkdownInlineKind.Link, segs[1].Kind);
        Assert.Equal("https://example.com/changes", segs[1].Url);
    }

    [Fact]
    public void Bare_url_inside_markdown_link_is_not_double_matched()
    {
        var segs = MarkdownInlineParser.Parse("[t](https://x.com/a)");
        Assert.Single(segs);
        Assert.Equal(MarkdownInlineKind.Link, segs[0].Kind);
    }

    [Fact]
    public void Non_web_url_kept_as_raw_text()
    {
        var segs = MarkdownInlineParser.Parse("[t](javascript:alert(1))");
        Assert.Single(segs);
        Assert.Equal(MarkdownInlineKind.Text, segs[0].Kind);
        Assert.Equal("[t](javascript:alert(1))", segs[0].Text);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Empty(MarkdownInlineParser.Parse(""));
    }
}

public class MarkdownInlineParserSnakeCaseTests
{
    [Fact]
    public void Intra_word_underscores_stay_plain()
    {
        var segs = MarkdownInlineParser.Parse("use some_var_name here");
        Assert.Single(segs);
        Assert.Equal(MarkdownInlineKind.Text, segs[0].Kind);
        Assert.Equal("use some_var_name here", segs[0].Text);
    }
}

public class MarkdownInlineParserEmojiFootnoteTests
{
    [Fact]
    public void Known_emoji_shortcode_becomes_emoji_text()
    {
        var segs = MarkdownInlineParser.Parse("fixed :bug: and :tada:");
        Assert.Equal(4, segs.Count);
        Assert.Equal("🐛", segs[1].Text);
        Assert.Equal("🎉", segs[3].Text);
    }

    [Fact]
    public void Plus_one_and_numeric_shortcodes_work()
    {
        var segs = MarkdownInlineParser.Parse(":+1: :100:");
        Assert.Equal("👍", segs[0].Text);
        Assert.Equal("💯", segs[2].Text);
    }

    [Fact]
    public void Unknown_shortcode_stays_raw_like_upstream()
    {
        var segs = MarkdownInlineParser.Parse(":notarealEmoji99:");
        Assert.Single(segs);
        Assert.Equal(":notarealEmoji99:", segs[0].Text);
    }

    [Fact]
    public void Emoji_table_is_the_upstream_markdown_package_table()
    {
        // dart-lang/markdown lib/src/emojis.dart, generated from
        // https://api.github.com/emojis (2022-05-03).
        Assert.True(EmojiTable.Emojis.Count > 1500);
        Assert.Equal("👍", EmojiTable.Emojis["+1"]);
        Assert.Equal("🎉", EmojiTable.Emojis["tada"]);
    }

    [Fact]
    public void Footnote_reference_is_its_own_segment()
    {
        var segs = MarkdownInlineParser.Parse("claim[^1] more");
        Assert.Equal(3, segs.Count);
        Assert.Equal(MarkdownInlineKind.FootnoteRef, segs[1].Kind);
        Assert.Equal("1", segs[1].Text);
    }

    [Fact]
    public void Footnote_reference_does_not_eat_links()
    {
        var segs = MarkdownInlineParser.Parse("[^1](https://x.com)");
        // footnote ref + leftover "(https://x.com)" as text, no crash
        Assert.Equal(MarkdownInlineKind.FootnoteRef, segs[0].Kind);
    }
}
