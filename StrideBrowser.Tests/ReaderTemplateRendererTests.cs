using StrideBrowser.Models.Reader;
using StrideBrowser.Services.Reader;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ReaderTemplateRendererTests
{
    private readonly IReaderTemplateRenderer _renderer = new ReaderTemplateRenderer();

    [Fact]
    public void Render_InlinesCssAndSanitizedContent()
    {
        var options = new ReaderOptions(FontScale: 1.2, Theme: "dark", ContentWidth: 720);
        var sanitized = "<p>Hello world</p>";
        var content = new ReaderContent("My Title", string.Empty, "https://example.com/article", DateTime.UtcNow);

        var html = _renderer.Render(content, options, sanitized);

        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains("img-src http: https:", html);
        Assert.DoesNotContain("data:", html.Split("Content-Security-Policy")[1].Split(">")[0]);
        Assert.Contains("style-src 'unsafe-inline'", html);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
        Assert.Contains("--reader-bg", html);
        Assert.Contains("My Title", html);
        Assert.Contains("<p>Hello world</p>", html);
        Assert.Contains("data-theme=\"dark\"", html);
        Assert.Contains("font-size: calc(1rem * 1.2)", html);
        Assert.Contains("max-width: 720px", html);
    }

    [Fact]
    public void Render_EncodesTitleAndByline()
    {
        var options = ReaderOptions.Default;
        var article = new ArticleResult("<script>alert(1)</script>", "By <b>me</b>", "", "<p>hi</p>", "site", 2, true);

        var html = _renderer.Render(article, "<p>hi</p>", options, "https://example.com");

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("By &lt;b&gt;me&lt;/b&gt;", html);
    }

    [Fact]
    public void Render_UsesSystemThemeByDefault()
    {
        var options = ReaderOptions.Default;
        var content = new ReaderContent("T", string.Empty, "https://example.com", DateTime.UtcNow);

        var html = _renderer.Render(content, options, "<p>x</p>");

        Assert.Contains("data-theme=\"system\"", html);
    }

    [Fact]
    public void Render_ArticleOverload_IncludesByline()
    {
        var options = new ReaderOptions(1.0, "light", 720);
        var article = new ArticleResult("Title", "Jane Doe", "excerpt", "<p>body</p>", "example.com", 100, true);

        var html = _renderer.Render(article, "<p>body</p>", options, "https://example.com/a");

        Assert.Contains("Jane Doe", html);
        Assert.Contains("Title", html);
    }
}
