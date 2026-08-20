using StrideBrowser.Services.Reader;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Scaffold for sanitizer contract tests. Real bodies in step 2.
/// Contract pinned in ADR v4: allow-list tags, drop style/srcset/on*, validate http or https only, block javascript: and data:.
/// These run under dotnet test with no WebView2.
/// </summary>
public sealed class ReaderSanitizerTests
{
    private readonly IReaderSanitizer _sanitizer = new ReaderSanitizer();

    [Fact(Skip = "sanitizer not implemented - scaffold")]
    public void Sanitize_BlocksJavascriptScheme()
    {
        var html = "<p>hi</p><a href=\"javascript:alert(1)\">x</a>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("javascript:", result);
    }

    [Fact(Skip = "sanitizer not implemented - scaffold")]
    public void Sanitize_BlocksDataImage()
    {
        var html = "<img src=\"data:image/png;base64,abc\">";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("data:", result);
    }

    [Fact(Skip = "sanitizer not implemented - scaffold")]
    public void Sanitize_StripsSrcsetAndStyle()
    {
        var html = "<img src=\"https://example.com/a.jpg\" srcset=\"https://example.com/a.jpg 1x\" style=\"color:red\">";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("srcset", result);
        Assert.DoesNotContain("style=", result);
    }

    [Fact(Skip = "sanitizer not implemented - scaffold")]
    public void Sanitize_StripsOnHandlers()
    {
        var html = "<p onclick=\"alert(1)\">hi</p>";
        var result = _sanitizer.Sanitize(html, "https://example.com/article");
        Assert.DoesNotContain("onclick", result);
    }
}
