using FakeNewsDetector.Services;
using Xunit;

namespace FakeNewsDetector.Tests;

public class TavilySnippetTests
{
    [Fact]
    public void CleanSnippet_StripsMarkdownHeadings()
    {
        var result = TavilyService.CleanSnippet("## Post. The Earth is flat, the Moon made of cheese.", 280);
        Assert.StartsWith("Post.", result);
        Assert.DoesNotContain("#", result);
    }

    [Fact]
    public void CleanSnippet_StripsEmphasisAndLinks()
    {
        var raw = "The **globe** theory is [debunked here](https://example.com/x) and _fully_ wrong.";
        var result = TavilyService.CleanSnippet(raw, 280);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("_", result);
        Assert.DoesNotContain("](", result);
        Assert.Contains("globe", result);
        Assert.Contains("debunked here", result); // link text kept, URL dropped
    }

    [Fact]
    public void CleanSnippet_CollapsesWhitespaceAndNewlines()
    {
        var result = TavilyService.CleanSnippet("Line one\n\n  Line   two\ttabbed", 280);
        Assert.Equal("Line one Line two tabbed", result);
    }

    [Fact]
    public void CleanSnippet_TruncatesAtWordBoundary()
    {
        var raw = string.Join(" ", System.Linq.Enumerable.Repeat("wordy", 100)); // ~600 chars
        var result = TavilyService.CleanSnippet(raw, 100);
        Assert.True(result.Length <= 101);         // <= maxLen + ellipsis
        Assert.EndsWith("…", result);
        Assert.DoesNotContain("word…", result);    // no mid-word cut
    }

    [Fact]
    public void CleanSnippet_ShortText_ReturnedAsIs()
    {
        var result = TavilyService.CleanSnippet("Short clean sentence.", 280);
        Assert.Equal("Short clean sentence.", result);
    }

    [Fact]
    public void CleanSnippet_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TavilyService.CleanSnippet("", 280));
        Assert.Equal(string.Empty, TavilyService.CleanSnippet("   ", 280));
    }
}
