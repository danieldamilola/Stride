using System.Text.Json;
using StrideBrowser.Engine;
using StrideBrowser.Helpers;
using StrideBrowser.Models.Reader;

namespace StrideBrowser.Services.Reader;

/// <summary>
/// Real extraction runs as JS inside the page WebView2 via ExecuteScriptAsync.
/// Uses the shared ReaderExtractor.js that exposes window.__strideReaderIsReadable and window.__strideReaderExtract.
/// The double JSON encoding from ExecuteScriptAsync is handled: outer JSON decodes to inner JSON.
/// </summary>
public sealed class ReaderExtractor : IReaderExtractor
{
    private readonly TabEngine _engine;

    public ReaderExtractor(TabEngine engine)
    {
        _engine = engine;
    }

    public async Task<bool> CanExtractAsync(Guid tabId)
    {
        var extractorJs = ResourceLoader.TryLoad("Resources.Reader.ReaderExtractor.js");
        if (extractorJs is null) return false;

        var script = extractorJs + "\nwindow.__strideReaderIsReadable();";
        var raw = await ExecuteWithTimeoutAsync(tabId, script, 2500);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        if (trimmed == "true") return true;
        if (trimmed == "false") return false;

        // ExecuteScriptAsync may return JSON-encoded string, try to deserialize
        try
        {
            var decoded = JsonSerializer.Deserialize<string>(trimmed);
            if (decoded is not null)
            {
                trimmed = decoded.Trim();
                if (trimmed == "true") return true;
                if (trimmed == "false") return false;
            }
        }
        catch { }

        // Fallback: try parse as JSON bool directly
        try
        {
            return JsonSerializer.Deserialize<bool>(trimmed);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ArticleResult> ExtractAsync(Guid tabId)
    {
        var extractorJs = ResourceLoader.TryLoad("Resources.Reader.ReaderExtractor.js");
        if (extractorJs is null) throw new InvalidOperationException("ReaderExtractor.js not found");

        var script = extractorJs + "\nwindow.__strideReaderExtract();";
        var raw = await ExecuteWithTimeoutAsync(tabId, script, 4000);
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("Extractor returned empty");

        // Outer decoding: ExecuteScriptAsync returns JSON-encoded string
        string inner;
        try
        {
            inner = JsonSerializer.Deserialize<string>(raw) ?? raw;
        }
        catch
        {
            inner = raw;
        }

        // Inner is JSON.stringify(...) of the article object
        try
        {
            var result = JsonSerializer.Deserialize<ArticleResultDto>(inner, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null) throw new InvalidOperationException("Extractor JSON was null");

            return new ArticleResult(
                result.Title ?? string.Empty,
                result.Byline ?? string.Empty,
                result.Excerpt ?? string.Empty,
                result.ContentHtml ?? string.Empty,
                result.SiteName ?? string.Empty,
                result.Length,
                result.IsReadable
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"ReaderExtractor deserialize failed: {ex.Message} rawInner={inner}");
            throw;
        }
    }

    private async Task<string> ExecuteWithTimeoutAsync(Guid tabId, string script, int timeoutMs)
    {
        var executeTask = _engine.ExecuteScriptAsync(tabId, script);
        var delayTask = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(executeTask, delayTask);
        if (completed == delayTask)
        {
            System.Diagnostics.Trace.WriteLine($"ReaderExtractor timeout after {timeoutMs}ms for tab {tabId}");
            return string.Empty;
        }

        try
        {
            return await executeTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"ReaderExtractor ExecuteScript failed: {ex.Message}");
            return string.Empty;
        }
    }

    private sealed class ArticleResultDto
    {
        public string? Title { get; set; }
        public string? Byline { get; set; }
        public string? Excerpt { get; set; }
        public string? ContentHtml { get; set; }
        public string? SiteName { get; set; }
        public int Length { get; set; }
        public bool IsReadable { get; set; }
    }
}
