using StrideBrowser.Models;
using StrideBrowser.Models.Reader;
using StrideBrowser.Services;
using StrideBrowser.Services.Reader;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ReaderRequestLifecycleTests
{
    private static readonly ArticleResult Article = new("Article", "", "", "<p>text</p>", "", 4, true);

    [Fact]
    public async Task RemovingTabDuringExtractionCannotRecreateItsSession()
    {
        var extractor = new Extractor();
        var service = Create(extractor);
        var tab = Guid.NewGuid();
        var pending = service.EnterReaderAsync(tab);
        service.RemoveSession(tab);
        extractor.Result.SetResult(Article);
        await pending;
        Assert.Null(service.GetSession(tab));
    }

    [Fact]
    public async Task ExitDuringBackgroundSanitizationPreventsEntry()
    {
        var sanitizer = new BlockingSanitizer();
        var extractor = new Extractor();
        extractor.Result.SetResult(Article);
        var service = Create(extractor, sanitizer);
        var tab = Guid.NewGuid();
        var pending = service.EnterReaderAsync(tab);
        await sanitizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ExitReaderAsync(tab);
        sanitizer.Release.Set();
        await pending;
        Assert.Null(service.GetSession(tab));
    }

    [Fact]
    public async Task NavigationDuringBackgroundWorkCannotPublishArticle()
    {
        string? url = "https://example.com/article";
        var sanitizer = new BlockingSanitizer();
        var extractor = new Extractor();
        extractor.Result.SetResult(Article);
        var service = Create(extractor, sanitizer, getUrl: _ => url);
        var tab = Guid.NewGuid();
        var pending = service.EnterReaderAsync(tab);
        await sanitizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        url = "https://example.com/other";
        sanitizer.Release.Set();
        await pending;
        Assert.Null(service.GetSession(tab));
    }

    [Fact]
    public async Task ChangingOptionsDuringEntryUsesLatestOptionsAndCallingTab()
    {
        var sanitizer = new BlockingSanitizer();
        var extractor = new Extractor();
        extractor.Result.SetResult(Article);
        var service = Create(extractor, sanitizer);
        var tab = Guid.NewGuid();
        var otherTab = Guid.NewGuid();
        var pending = service.EnterReaderAsync(tab);
        await sanitizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.UpdateOptionsAsync(ReaderOptions.Default with { Theme = "dark" });
        sanitizer.Release.Set();
        var content = await pending;
        Assert.Equal("dark:<p>text</p>", content.Html);
        Assert.True(service.GetSession(tab)!.IsInReader);
        Assert.Null(service.GetSession(otherTab));
    }

    [Fact]
    public async Task TransferDuringExtractionInvalidatesTheOldOwnersWork()
    {
        var extractor = new Extractor();
        var source = Create(extractor);
        var destination = Create(new Extractor());
        var tab = Guid.NewGuid();
        var pending = source.EnterReaderAsync(tab);
        source.TransferSessionTo(destination, tab);
        extractor.Result.SetResult(Article);
        await pending;
        Assert.Null(source.GetSession(tab));
        Assert.Null(destination.GetSession(tab));
    }

    [Fact]
    public async Task SanitizerFailureLeavesOriginalPageAndSessionUntouched()
    {
        var extractor = new Extractor();
        extractor.Result.SetResult(Article with { ContentHtml = new string('x', ReaderSanitizer.MaxInputCharacters + 1) });
        var service = Create(extractor);
        var tab = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnterReaderAsync(tab));
        Assert.Null(service.GetSession(tab));
    }

    [Fact]
    public async Task ExtractionTimeoutCannotPublishALateResult()
    {
        var extractor = new Extractor();
        var service = Create(extractor);
        var tab = Guid.NewGuid();
        await Assert.ThrowsAsync<TimeoutException>(() => service.EnterReaderAsync(tab));
        extractor.Result.SetResult(Article);
        Assert.Null(service.GetSession(tab));
    }

    [Fact]
    public async Task ExitDuringRefreshCannotRestoreReaderState()
    {
        var extractor = new Extractor();
        extractor.Result.SetResult(Article);
        var renderer = new GateRenderer(2);
        var service = Create(extractor, renderer: renderer);
        var tab = Guid.NewGuid();
        var original = await service.EnterReaderAsync(tab);
        var pending = service.RefreshAsync(tab);
        await renderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ExitReaderAsync(tab);
        renderer.Release.Set();
        await pending;
        Assert.False(service.GetSession(tab)!.IsInReader);
        Assert.Equal(original, service.GetSession(tab)!.Current);
    }

    [Fact]
    public async Task OptionChangeDuringRenderDiscardsOldRendering()
    {
        var extractor = new Extractor();
        extractor.Result.SetResult(Article);
        var renderer = new GateRenderer(1);
        var service = Create(extractor, renderer: renderer);
        var tab = Guid.NewGuid();
        var pending = service.EnterReaderAsync(tab);
        await renderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.UpdateOptionsAsync(ReaderOptions.Default with { Theme = "dark" });
        renderer.Release.Set();
        Assert.Equal("dark:<p>text</p>", (await pending).Html);
    }

    private sealed class GateRenderer(int blockOnCall) : IReaderTemplateRenderer
    {
        private int _calls;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        public string Render(ReaderContent content, ReaderOptions options, string html) => options.Theme + ":" + html;
        public string Render(ArticleResult article, string html, ReaderOptions options, string sourceUrl)
        {
            if (Interlocked.Increment(ref _calls) == blockOnCall)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return options.Theme + ":" + html;
        }
    }

    private static ReaderService Create(Extractor extractor, IReaderSanitizer? sanitizer = null,
        IReaderTemplateRenderer? renderer = null, Func<Guid, string?>? getUrl = null) =>
        new(new BrowserSettings(), new Settings(), extractor, sanitizer ?? new ReaderSanitizer(), renderer ?? new Renderer(),
            getUrl ?? (_ => "https://example.com/article"));

    private sealed class Extractor : IReaderExtractor
    {
        internal TaskCompletionSource<ArticleResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> CanExtractAsync(Guid tabId) => Task.FromResult(true);
        public Task<ArticleResult> ExtractAsync(Guid tabId) => Result.Task;
    }
    private sealed class BlockingSanitizer : IReaderSanitizer
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        public string Sanitize(string html, string baseUrl)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return html;
        }
    }
    private sealed class Renderer : IReaderTemplateRenderer
    {
        public string Render(ReaderContent content, ReaderOptions options, string html) => options.Theme + ":" + html;
        public string Render(ArticleResult article, string html, ReaderOptions options, string sourceUrl) => options.Theme + ":" + html;
    }
    private sealed class Settings : ISettingsStore
    {
        public BrowserSettings Load() => new();
        public bool TrySave(BrowserSettings settings) => true;
        public void Save(BrowserSettings settings) { }
    }
}
