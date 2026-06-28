using FakeNewsDetector.Services;
using Xunit;

namespace FakeNewsDetector.Tests;

public class HtmlExtractorTests
{
    // ── Title extraction ────────────────────────────────────────────────────

    [Fact]
    public void ExtractTextAndTitle_ReadsTitle()
    {
        var html = "<html><head><title>Breaking News Today</title></head><body><p>Content.</p></body></html>";
        var (_, title) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Equal("Breaking News Today", title);
    }

    [Fact]
    public void ExtractTextAndTitle_FallsBackToH1WhenTitleIsGeneric()
    {
        var html = "<html><head><title>Home</title></head><body><h1>Real Article Headline</h1><p>Text.</p></body></html>";
        var (_, title) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Equal("Real Article Headline", title);
    }

    [Fact]
    public void ExtractTextAndTitle_EmptyTitleWhenNone()
    {
        var html = "<html><body><p>No title tag.</p></body></html>";
        var (_, title) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Equal("", title);
    }

    // ── Text extraction ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractTextAndTitle_ExtractsParagraphs()
    {
        var html = "<html><body><p>First paragraph.</p><p>Second paragraph.</p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Contains("First paragraph", text);
        Assert.Contains("Second paragraph", text);
    }

    [Fact]
    public void ExtractTextAndTitle_StripsTags()
    {
        var html = "<html><body><p>Hello <b>world</b>.</p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.DoesNotContain("<b>", text);
        Assert.Contains("Hello", text);
        Assert.Contains("world", text);
    }

    [Fact]
    public void ExtractTextAndTitle_RemovesScriptContent()
    {
        var html = "<html><body><script>alert('xss')</script><p>Article body.</p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.DoesNotContain("alert", text);
        Assert.Contains("Article body", text);
    }

    [Fact]
    public void ExtractTextAndTitle_RemovesStyleContent()
    {
        var html = "<html><head><style>.red{color:red}</style></head><body><p>Clean text.</p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.DoesNotContain(".red", text);
        Assert.Contains("Clean text", text);
    }

    [Fact]
    public void ExtractTextAndTitle_DecodesHtmlEntities()
    {
        var html = "<html><body><p>France &amp; Germany signed a deal worth &euro;1bn.</p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Contains("France & Germany", text);
    }

    [Fact]
    public void ExtractTextAndTitle_CollapsesWhitespace()
    {
        var html = "<html><body><p>  Too    many   spaces.  </p></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.DoesNotContain("   ", text);
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractTextAndTitle_EmptyHtml_DoesNotThrow()
    {
        var (text, title) = HtmlExtractor.ExtractTextAndTitle("");
        Assert.NotNull(text);
        Assert.NotNull(title);
    }

    [Fact]
    public void ExtractTextAndTitle_NoBody_UsesRawText()
    {
        var html = "<title>Bare doc</title><p>Some text.</p>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void ExtractTextAndTitle_ArticleTag_ContentIncluded()
    {
        var html = "<html><body><article>This is the main article content.</article></body></html>";
        var (text, _) = HtmlExtractor.ExtractTextAndTitle(html);
        Assert.Contains("main article content", text);
    }
}
