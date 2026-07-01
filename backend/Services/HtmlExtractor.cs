using HtmlAgilityPack;
using System.Text;

namespace FakeNewsDetector.Services;

public static class HtmlExtractor
{
    public static (string Text, string Title) ExtractTextAndTitle(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Grab OG/meta fields early — these are the fallback for paywalled pages
            var ogTitle   = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "")?.Trim() ?? "";
            var ogDesc    = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", "")?.Trim() ?? "";
            var metaDesc  = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "")?.Trim() ?? "";

            // Title: OG title beats <title> tag (cleaner, no site-name suffix)
            var rawTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? "";
            var title = !string.IsNullOrEmpty(ogTitle) ? ogTitle : rawTitle;
            if (string.IsNullOrEmpty(title) || title.Contains("Home") || title.Contains("Index"))
                title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? title;

            // Remove boilerplate nodes before text extraction
            var noise = doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer|//aside|//form|//noscript");
            if (noise != null) foreach (var n in noise.ToList()) n.Remove();

            // Prefer <article> → <main> → <body> for article content
            var contentRoot = doc.DocumentNode.SelectSingleNode("//article")
                           ?? doc.DocumentNode.SelectSingleNode("//main")
                           ?? doc.DocumentNode.SelectSingleNode("//body");
            if (contentRoot == null) return (doc.DocumentNode.InnerText, title);

            var contentNodes = contentRoot.SelectNodes(".//p|.//h1|.//h2|.//h3|.//h4|.//blockquote");
            var sb = new StringBuilder();
            if (contentNodes != null)
                foreach (var node in contentNodes)
                {
                    var txt = node.InnerText.Trim();
                    if (txt.Length > 3) // skip blank / single-char fragments
                        sb.AppendLine(txt);
                }

            var text = System.Net.WebUtility.HtmlDecode(sb.ToString());
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            // If no <p>/<h*> children were found (e.g. <article>bare text</article>),
            // fall back to the content root's own inner text.
            if (string.IsNullOrWhiteSpace(text))
            {
                text = System.Net.WebUtility.HtmlDecode(contentRoot.InnerText);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            }

            // Paywall / JS-rendered page fallback: if article body is thin, use OG description
            if (text.Length < 300)
            {
                var meta = !string.IsNullOrEmpty(ogDesc) ? ogDesc : metaDesc;
                if (!string.IsNullOrEmpty(meta))
                    text = string.IsNullOrEmpty(text) ? meta : $"{text} {meta}";
            }

            return (text, title);
        }
        catch
        {
            return (html, "");
        }
    }
}
