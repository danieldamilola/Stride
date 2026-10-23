using System.IO;
using System.Net;
using System.Net.Http;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class FaviconBudgetTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stride-favicon-budget");
    public void Dispose()
    {
        try { _root.Delete(true); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Favicon test cleanup failed: {ex}"); }
    }

    [Fact]
    public async Task FailedCacheWriteReleasesTheGateAndAllowsLaterWrites()
    {
        var directory = Path.Combine(_root.FullName, "cache");
        await File.WriteAllTextAsync(directory, "blocks directory creation");
        var cache = new FaviconDiskCache(directory);
        await cache.WriteAsync("host", [1, 2, 3]).WaitAsync(TimeSpan.FromSeconds(5));
        File.Delete(directory);
        await cache.WriteAsync("host", [1, 2, 3]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(directory, "host.png")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task NonSeekableOversizedStreamStopsAfterOneByteBeyondTheLimit()
    {
        using var stream = new CountingStream(new byte[FaviconLoader.MaxIconBytes + 100_000]);
        Assert.Null(await FaviconLoader.ReadBoundedBytesAsync(stream));
        Assert.Equal(FaviconLoader.MaxIconBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task SeekableOversizedStreamIsRejectedBeforeReading()
    {
        using var stream = new MemoryStream(new byte[FaviconLoader.MaxIconBytes + 1]);
        Assert.Null(await FaviconLoader.ReadBoundedBytesAsync(stream));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ExactSizeLimitIsAccepted()
    {
        var bytes = new byte[FaviconLoader.MaxIconBytes];
        using var stream = new CountingStream(bytes);
        Assert.Equal(bytes, await FaviconLoader.ReadBoundedBytesAsync(stream));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedProxyResponseIsNeverWritten(bool knownLength)
    {
        var dir = _root.CreateSubdirectory(Guid.NewGuid().ToString()).FullName;
        var bytes = new byte[FaviconLoader.MaxIconBytes + 1234];
        var content = knownLength ? (HttpContent)new ByteArrayContent(bytes) : new StreamContent(new CountingStream(bytes));
        using var http = new HttpClient(new Handler(content));
        var loader = new FaviconLoader(new BrowserSettings { FaviconProxyEnabled = true }, http, dir);
        Assert.Null(await loader.LoadAsync("https://large.example.org/"));
        Assert.Empty(Directory.GetFiles(dir, "*.png"));
    }

    [Fact]
    public async Task OversizedDiskFileIsFoundWithoutBufferingItsBytes()
    {
        var path = Path.Combine(_root.FullName, "cached.example.org.png");
        await File.WriteAllBytesAsync(path, new byte[FaviconLoader.MaxIconBytes + 1]);
        // Insert after startup pruning so this directly exercises the bounded disk read.
        var disk = new FaviconDiskCache(_root.FullName);
        await disk.PruneAsync();
        await File.WriteAllBytesAsync(path, new byte[FaviconLoader.MaxIconBytes + 1]);
        var read = await disk.ReadAsync("cached.example.org");
        Assert.True(read.Found);
        Assert.Null(read.Bytes);
    }

    [Fact]
    public async Task RetentionKeepsNewestFilesWithinCountAndByteBudgets()
    {
        var cache = new FaviconDiskCache(_root.FullName, maxEntries: 2, maxBytes: 12);
        await cache.PruneAsync();
        Seed("old", 4, DateTime.UtcNow.AddMinutes(-3));
        Seed("middle", 7, DateTime.UtcNow.AddMinutes(-2));
        Seed("new", 7, DateTime.UtcNow.AddMinutes(-1));
        await cache.PruneAsync();
        var files = _root.GetFiles("*.png");
        Assert.Equal(new[] { "new.png", "old.png" }, files.Select(f => f.Name).OrderBy(x => x).ToArray());
        Assert.True(files.Sum(f => f.Length) <= 12);
    }

    [Fact]
    public async Task CountBudgetRemovesOldestFiles()
    {
        var cache = new FaviconDiskCache(_root.FullName, maxEntries: 2);
        await cache.PruneAsync();
        Seed("old", 1, DateTime.UtcNow.AddMinutes(-3));
        Seed("middle", 1, DateTime.UtcNow.AddMinutes(-2));
        Seed("new", 1, DateTime.UtcNow.AddMinutes(-1));
        await cache.PruneAsync();
        Assert.False(File.Exists(Path.Combine(_root.FullName, "old.png")));
        Assert.Equal(2, _root.GetFiles("*.png").Length);
    }

    [Fact]
    public async Task ExpirationOnlyRemovesIconsInTheSelectedProfile()
    {
        var other = _root.CreateSubdirectory("another-profile");
        var otherFile = Path.Combine(other.FullName, "untouched.png");
        File.WriteAllBytes(otherFile, [1]);
        File.SetLastWriteTimeUtc(otherFile, DateTime.UtcNow.AddDays(-90));
        Seed("expired", 1, DateTime.UtcNow.AddDays(-31));
        Seed("recent", 1, DateTime.UtcNow);
        var cache = new FaviconDiskCache(_root.FullName);
        await cache.PruneAsync();
        Assert.False(File.Exists(Path.Combine(_root.FullName, "expired.png")));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "recent.png")));
        Assert.True(File.Exists(otherFile));
    }

    [Fact]
    public async Task LocalDiskHitRenewsRetentionWithoutNetworkOrDecode()
    {
        var cache = new FaviconDiskCache(_root.FullName, maxEntries: 2);
        await cache.PruneAsync();
        Seed("hit", 1, DateTime.UtcNow.AddDays(-29));
        Seed("other", 1, DateTime.UtcNow.AddMinutes(-2));
        Seed("new", 1, DateTime.UtcNow.AddMinutes(-1));
        Assert.True((await cache.ReadAsync("hit")).Found);
        await cache.PruneAsync();
        Assert.True(File.Exists(Path.Combine(_root.FullName, "hit.png")));
        Assert.False(File.Exists(Path.Combine(_root.FullName, "other.png")));
    }

    [Fact]
    public async Task ConcurrentWritersAndPrunersKeepWholeFilesAndRespectBudget()
    {
        var first = new FaviconDiskCache(_root.FullName, maxEntries: 10);
        var second = new FaviconDiskCache(_root.FullName, maxEntries: 10);
        var bytes = Enumerable.Repeat((byte)19, 4096).ToArray();
        var writes = Enumerable.Range(0, 40).Select(i => (i % 2 == 0 ? first : second).WriteAsync($"host{i}", bytes));
        await Task.WhenAll(writes.Append(first.PruneAsync()).Append(second.PruneAsync()));
        await first.PruneAsync();
        var files = _root.GetFiles("*.png");
        Assert.Equal(10, files.Length);
        foreach (var file in files) Assert.Equal(bytes, await File.ReadAllBytesAsync(file.FullName));
        Assert.Empty(_root.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task LocalMissDoesNotPreventLaterNativeDiskFill()
    {
        using var http = new HttpClient(new Handler(new ByteArrayContent([1])));
        var loader = new FaviconLoader(new BrowserSettings(), http, _root.FullName);
        Assert.Null(await loader.LoadAsync("https://native.example.org/", FaviconLoadScope.LocalListRow));
        var disk = new FaviconDiskCache(_root.FullName);
        await disk.WriteAsync("native.example.org", [3, 4, 5]);
        Assert.True((await disk.ReadAsync("native.example.org")).Found);
        await loader.LoadAsync("https://native.example.org/", FaviconLoadScope.LocalListRow);
        Assert.Equal(new byte[] { 3, 4, 5 }, await File.ReadAllBytesAsync(Path.Combine(_root.FullName, "native.example.org.png")));
    }

    private void Seed(string host, int length, DateTime lastUse)
    {
        var path = Path.Combine(_root.FullName, host + ".png");
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, lastUse);
    }
    private sealed class Handler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = base.Read(buffer.Span);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }
}

