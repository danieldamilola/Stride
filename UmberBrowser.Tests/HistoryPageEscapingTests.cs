using UmberBrowser.Helpers;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The history page renders remote-controlled data (a visited page's URL and title) with
/// client-side innerHTML. Two defects were fixed here; these tests pin both so the escaping
/// cannot silently regress.
/// </summary>
public sealed class HistoryPageEscapingTests
{
    private const string TemplateResource = "Resources.Pages.History.html";

    private static string Template() => ResourceLoader.Load(TemplateResource);

    /// <summary>
    /// A page controls its own fragment, and neither Chromium's fragment percent-encode set
    /// nor System.Uri.ToString() encodes an apostrophe. So a URL in history can contain a raw
    /// <c>'</c>. Building <c>onclick="openUrl('...')"</c> from it closed the JS string literal
    /// and executed the remainder as script inside a privileged internal page that holds the
    /// session IPC token. The URL must travel in a data attribute instead.
    /// </summary>
    [Fact]
    public void EntryTitles_DoNotInlineHistoryUrlsIntoEventHandlerAttributes()
    {
        var template = Template();

        Assert.DoesNotContain("onclick=\"openUrl(", template);
        Assert.DoesNotContain("onclick='openUrl(", template);
    }

    [Fact]
    public void EntryTitles_CarryUrlInADataAttribute()
    {
        var template = Template();

        Assert.Contains("data-url=\"${escapeHtml(e.Url)}\"", template);
        Assert.Contains("closest('a.entry-title[data-url]')", template);
    }

    /// <summary>
    /// The old escaper relied on textContent -&gt; innerHTML, which the HTML fragment
    /// serialization algorithm only escapes for &amp; &lt; &gt; and U+00A0. Quotes passed
    /// through untouched, which is what made the attribute breakout above possible.
    /// </summary>
    [Fact]
    public void EscapeHtml_CoversQuotes_NotJustAngleBrackets()
    {
        var template = Template();
        var start = template.IndexOf("function escapeHtml", System.StringComparison.Ordinal);
        Assert.True(start > 0, "escapeHtml not found in History.html");

        var body = template.Substring(start, Math.Min(400, template.Length - start));

        Assert.Contains("&quot;", body);
        Assert.Contains("&#39;", body);
        Assert.DoesNotContain("createElement('div')", body);
    }

    /// <summary>
    /// The favicon host reaches a URL path segment inside a double-quoted src attribute and is
    /// never escaped on this page. Percent-encoding is the correct encoding for a path segment
    /// and removes any chance of breaking out of the attribute.
    /// </summary>
    [Fact]
    public void FaviconHost_IsPercentEncodedRatherThanInterpolatedRaw()
    {
        var template = Template();

        Assert.DoesNotContain("icons.duckduckgo.com/ip3/${domain}.ico", template);
        Assert.Contains("encodeURIComponent(domain)", template);
    }
}