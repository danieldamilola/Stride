using StrideBrowser.Services.Reader;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ReaderSanitizerTests
{
    private readonly IReaderSanitizer _sanitizer = new ReaderSanitizer();

    [Fact]
    public void Sanitize_BlocksJavascriptScheme()
    {
        var html = "<p>hi</p><a href=\"javascript:alert(1)\">x</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("javascript:", result);
        Assert.DoesNotContain("href=", result);
        Assert.Contains("<p>hi</p>", result);
    }

    [Fact]
    public void Sanitize_BlocksDataImage()
    {
        var html = "<img src=\"data:image/png;base64,abc\">";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("data:", result);
    }

    [Fact]
    public void Sanitize_StripsSrcsetAndStyle()
    {
        var html = "<img src=\"https://example.com/a.jpg\" srcset=\"https://example.com/a.jpg 1x\" style=\"color:red\">";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("srcset", result);
        Assert.DoesNotContain("style=", result);
        Assert.Contains("https://example.com/a.jpg", result);
    }

    [Fact]
    public void Sanitize_StripsOnHandlers()
    {
        var html = "<p onclick=\"alert(1)\">hi</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("onclick", result);
        Assert.Contains("<p>hi</p>", result);
    }

    [Fact]
    public void Sanitize_AllowsHttpAndHttps()
    {
        var html = "<a href=\"https://example.com/page\">link</a><a href=\"http://example.com/other\">other</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.Contains("https://example.com/page", result);
        Assert.Contains("http://example.com/other", result);
    }

    [Fact]
    public void Sanitize_ResolvesRelativeUrl()
    {
        var html = "<a href=\"/page\">link</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.Contains("https://example.com/page", result);
    }

    [Fact]
    public void Sanitize_ResolvesProtocolRelativeUrl()
    {
        var html = "<a href=\"//other.com/page\">link</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.Contains("https://other.com/page", result);
    }

    [Fact]
    public void Sanitize_BlocksFileScheme()
    {
        var html = "<a href=\"file:///etc/passwd\">x</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("file:", result);
    }

    [Fact]
    public void Sanitize_DropsScriptAndStyleEntirely()
    {
        var html = "<p>before</p><script>alert(1)</script><style>body{}</style><p>after</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("script", result);
        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("style", result);
        Assert.Contains("<p>before</p>", result);
        Assert.Contains("<p>after</p>", result);
    }

    [Fact]
    public void Sanitize_DropsIframeEntirely()
    {
        var html = "<p>hi</p><iframe src=\"https://evil.com\"></iframe><p>bye</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("iframe", result);
        Assert.DoesNotContain("evil.com", result);
    }

    [Fact]
    public void Sanitize_StripsDisallowedTagsButKeepsContent()
    {
        var html = "<div><section><p>hello</p></section></div>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("<div", result);
        Assert.DoesNotContain("<section", result);
        Assert.Contains("<p>hello</p>", result);
    }

    [Fact]
    public void Sanitize_StripsComments()
    {
        var html = "<p>hi</p><!-- comment --><p>bye</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("comment", result);
        Assert.Contains("<p>hi</p>", result);
    }

    [Fact]
    public void Sanitize_StripsXmlnsAndSrcdoc()
    {
        var html = "<p xmlns=\"http://example.com\">hi</p><iframe srcdoc=\"<p>evil</p>\"></iframe>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("xmlns", result);
        Assert.DoesNotContain("srcdoc", result);
    }

    [Fact]
    public void Sanitize_KeepsAltAndTitle()
    {
        var html = "<img src=\"https://example.com/a.jpg\" alt=\"photo\" title=\"my photo\">";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.Contains("alt=\"photo\"", result);
        Assert.Contains("title=\"my photo\"", result);
    }

    [Fact]
    public void Sanitize_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, _sanitizer.Sanitize(string.Empty, "https://example.com/article"));
        Assert.Equal(string.Empty, _sanitizer.Sanitize(null!, "https://example.com/article"));
    }

    [Fact]
    public void Sanitize_BlocksVbscriptScheme()
    {
        var html = "<a href=\"vbscript:msgbox(1)\">x</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("vbscript:", result);
    }

    [Fact]
    public void Sanitize_DropsClassAndId()
    {
        var html = "<p class=\"evil\" id=\"x\">hi</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("class=", result);
        Assert.DoesNotContain("id=", result);
        Assert.Contains("<p>hi</p>", result);
    }
}
