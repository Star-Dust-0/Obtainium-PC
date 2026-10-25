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
