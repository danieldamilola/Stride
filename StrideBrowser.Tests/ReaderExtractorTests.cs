using StrideBrowser.Tests.Fixtures;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Reader JS heuristics tests. These must run through a real CoreWebView2 instance
/// against canned sample HTML, not through C# DOM duplication and not through jsdom.
/// Scaffold only. Real harness is WebView2Fixture with STA, hidden HWND, scoped UserDataFolder.
/// </summary>
[Collection("WebView2")]
public sealed class ReaderExtractorTests : IClassFixture<WebView2Fixture>
{
    private readonly WebView2Fixture _fixture;

    public ReaderExtractorTests(WebView2Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "harness pending - STA and hidden HWND plumbing not yet implemented")]
    public async Task Extractor_ClassifiesArticle_AsReadable()
    {
        // Real implementation:
        // var controller = await _fixture.CreateControllerAsync();
        // await NavigateAndWaitAsync(controller, SamplePath("article.html"));
        // var script = File.ReadAllText("Resources/Reader/ReaderExtractor.js");
        // var raw = await controller.CoreWebView2.ExecuteScriptAsync(script + "; JSON.stringify(window.__strideReaderExtract())");
        // var json = JsonSerializer.Deserialize<string>(raw); // ExecuteScriptAsync double encodes
        // Assert.Contains("\"isReadable\":true", json);
        await Task.CompletedTask;
    }

    [Fact(Skip = "harness pending - STA and hidden HWND plumbing not yet implemented")]
    public async Task Extractor_ClassifiesHomepage_AsNotReadable()
    {
        await Task.CompletedTask;
    }
}
