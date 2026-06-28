using HtmlAgilityPack;
using System.Text;

namespace FakeNewsDetector.Services;

/// <summary>
/// Extracts plain text and the page title from raw HTML.
/// Isolated here so it can be unit-tested independently of the controller.
/// </summary>
public static class HtmlExtractor
{
    public static (string Text, string Title) ExtractTextAndTitle(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? "";
            if (string.IsNullOrEmpty(title) || title.Contains("Home") || title.Contains("Index"))
                title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? title;

            var scripts = doc.DocumentNode.SelectNodes("//script|//style");
            if (scripts != null) foreach (var node in scripts) node.Remove();

            var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode == null) return (doc.DocumentNode.InnerText, title);

            var contentNodes = bodyNode.SelectNodes("//p|//h1|//h2|//h3|//h4|//h5|//h6|//article|//section");
            if (contentNodes == null || contentNodes.Count == 0) return (bodyNode.InnerText, title);

            var sb = new StringBuilder();
            foreach (var node in contentNodes) sb.AppendLine(node.InnerText.Trim());

            var text = System.Net.WebUtility.HtmlDecode(sb.ToString());
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            return (text, title);
        }
        catch
        {
            return (html, "");
        }
    }
}
