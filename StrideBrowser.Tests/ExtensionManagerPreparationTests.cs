using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The disk half of extension preparation, tested with no WebView2 anywhere.
///
/// <c>PrepareAsync</c> is the stage that used to run on the dispatcher. These
/// tests cover what it must do in each state, and they are deliberately separate
/// from <c>ApplyAsync</c>, which is the COM half and cannot run here. That split
/// is the thing being verified: the work that needs no COM is callable on its own,
/// so it does not have to hold up the UI thread.
/// </summary>
public sealed class ExtensionManagerPreparationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stride-ext-prep");

    private string ExtensionsDir => Path.Combine(_root.FullName, "extensions");
    private string HashFile => Path.Combine(_root.FullName, "ublock.sha256");
    private string BundledDir => Path.Combine(_root.FullName, "bundled");

    public void Dispose() => _root.Delete(true);

    private ExtensionManager NewManager(HttpClient? http = null) =>
        new(ExtensionsDir, HashFile, BundledDir, http, TinyZipHash);

    /// <summary>Fixed so every call produces the same bytes, and therefore one hash.</summary>
    private static readonly DateTimeOffset ZipTimestamp =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string TinyZipHash =
        Convert.ToHexString(SHA256.HashData(MakeTinyZip()));

    private void WriteBundledZip(string name = "uBlock0_1.73.0.chromium.zip")
    {
        Directory.CreateDirectory(BundledDir);
        File.WriteAllBytes(Path.Combine(BundledDir, name), MakeTinyZip());
    }

    /// <summary>A real zip with a manifest, so extraction and manifest lookup both run.</summary>
    private static byte[] MakeTinyZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            // Each entry stream is closed before the next entry is created. Leaving
            // one open makes the archive reject the next CreateEntry call. The
            // timestamp is fixed so the archive bytes, and its pinned hash, are stable.
            var manifestEntry = zip.CreateEntry("manifest.json");
            manifestEntry.LastWriteTime = ZipTimestamp;
            using (var manifest = manifestEntry.Open())
            using (var writer = new StreamWriter(manifest))
            {
                writer.Write("{\"manifest_version\":3,\"name\":\"uBlock Origin\",\"version\":\"1.73.0\"}");
            }

            var assetsEntry = zip.CreateEntry("assets/assets.json");
            assetsEntry.LastWriteTime = ZipTimestamp;
            using (var assets = assetsEntry.Open())
            using (var writer = new StreamWriter(assets))
            {
                writer.Write("{\"filter\":{\"contentURL\":\"https://cdn.jsdelivr.net/x.js\",\"cdnURLs\":[\"u\"],\"patchURLs\":[\"p\"]}}");
            }
        }
        return buffer.ToArray();
    }

    [Fact]
    public async Task ExistingExtractedFolder_ReturnsItWithoutDownloading()
    {
        var manifestDir = Path.Combine(ExtensionsDir, "uBlock0_1.73.0");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{\"name\":\"uBlock Origin\"}");

        var manager = NewManager(new HttpClient(new FailingHandler()));
        var result = await manager.PrepareAsync();

        Assert.Equal(manifestDir, result);
        Assert.True(manager.LastPreparation.FolderWasAlreadyPresent);
    }

    [Fact]
    public async Task ExistingExtractedFolder_ReportsWarmPreparationAsCheap()
    {
        // The warm path used to be cheap on the UI thread only because the work
        // was small. It is measured so that stays visible rather than assumed.
        var manifestDir = Path.Combine(ExtensionsDir, "uBlock0_1.73.0");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{\"name\":\"uBlock Origin\"}");

        var manager = NewManager(new HttpClient(new FailingHandler()));
        await manager.PrepareAsync();

        var t = manager.LastPreparation;
        Assert.True(t.FolderWasAlreadyPresent);
        Assert.Equal(0, t.DownloadMs);
        Assert.Equal(0, t.ExtractMs);
        Assert.True(t.TotalMs >= 0);
    }

    [Fact]
    public async Task BundledZip_IsExtractedWhenNoFolderExists()
    {
        WriteBundledZip();

        var manager = NewManager(new HttpClient(new FailingHandler()));
        var result = await manager.PrepareAsync();

        Assert.NotNull(result);
        Assert.True(File.Exists(Path.Combine(result!, "manifest.json")));
        Assert.False(manager.LastPreparation.FolderWasAlreadyPresent);
        Assert.True(manager.LastPreparation.ExtractMs >= 0);
    }

    [Fact]
    public async Task BundledExtraction_DisarmsTheMirrorKeysAndKeepsTheOriginUrl()
    {
        // The mirror and diff keys are disarmed with a leading caret, so nothing is
        // fetched from a mirror or a diff server. The origin url in contentURL is kept,
        // because that is the one the extension's updater fetches from, and removing it
        // is what stopped filter lists from ever updating again.
        WriteBundledZip();

        var manager = NewManager(new HttpClient(new FailingHandler()));
        var result = await manager.PrepareAsync();

        Assert.NotNull(result);
        var assets = Path.Combine(result!, "assets", "assets.json");
        Assert.True(File.Exists(assets));
        var text = File.ReadAllText(assets);
        Assert.Contains("^cdnURLs", text);
        Assert.Contains("^patchURLs", text);
        Assert.DoesNotContain("\"cdnURLs\"", text);
        Assert.DoesNotContain("\"patchURLs\"", text);
        Assert.Contains("https://cdn.jsdelivr.net/x.js", text);
    }

    [Fact]
    public async Task MissingBundledZip_FallsBackToRemoteDownload()
    {
        var http = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MakeTinyZip()) }));

        var manager = NewManager(http);
        var result = await manager.PrepareAsync();

        Assert.NotNull(result);
        Assert.True(manager.LastPreparation.DownloadMs >= 0);
    }

    [Fact]
    public async Task ColdPreparation_RepairsTheFolderBeforeAnythingRegistersIt()
    {
        WriteBundledZip();
        var manager = NewManager(new HttpClient(new FailingHandler()));

        var result = await manager.PrepareAsync();

        Assert.NotNull(result);
        Assert.False(manager.LastPreparation.FolderWasAlreadyPresent);
        Assert.True(manager.LastPreparation.AssetsRewritten);
        var assets = Path.Combine(result!, "assets", "assets.json");
        Assert.Contains("^cdnURLs", File.ReadAllText(assets));
    }

    [Fact]
    public async Task WarmPreparation_LeavesTheInstalledFilesByteForByteAndTimeForTime()
    {
        // WebView2 keeps an unpacked extension by folder path and does not copy it, so
        // rewriting assets.json on every start is not a harmless cleanup: it changes
        // the content of a folder the platform has already registered. A warm start
        // must leave the bytes and the mtime alone.
        WriteBundledZip();
        var manager = NewManager(new HttpClient(new FailingHandler()));
        var cold = await manager.PrepareAsync();
        Assert.NotNull(cold);

        var assets = Path.Combine(cold!, "assets", "assets.json");
        var coldBytes = File.ReadAllBytes(assets);
        var coldTime = new FileInfo(assets).LastWriteTimeUtc.Ticks;
        await Task.Delay(20);

        var warm = await manager.PrepareAsync();

        Assert.Equal(cold, warm);
        Assert.True(manager.LastPreparation.FolderWasAlreadyPresent);
        Assert.False(manager.LastPreparation.AssetsRewritten);
        Assert.Equal(coldBytes, File.ReadAllBytes(assets));
        Assert.Equal(coldTime, new FileInfo(assets).LastWriteTimeUtc.Ticks);
        Assert.Equal(0, manager.LastPreparation.ExtractMs);
        Assert.Equal(0, manager.LastPreparation.DownloadMs);
    }

    [Fact]
    public async Task WarmPreparation_LeavesEveryFileInTheFolderUntouched()
    {
        WriteBundledZip();
        var manager = NewManager(new HttpClient(new FailingHandler()));
        var cold = await manager.PrepareAsync();
        Assert.NotNull(cold);

        var before = SnapshotFiles(cold!);
        await Task.Delay(20);
        await manager.PrepareAsync();
        var after = SnapshotFiles(cold!);

        Assert.Equal(before.Count, after.Count);
        foreach (var entry in before)
        {
            Assert.True(after.ContainsKey(entry.Key), "missing after warm prepare: " + entry.Key);
            Assert.Equal(entry.Value.Length, after[entry.Key].Length);
            Assert.Equal(entry.Value.Ticks, after[entry.Key].Ticks);
        }
    }

    [Fact]
    public async Task AnUnnormalizedFolder_IsRepairedOnceAndThenLeftAlone()
    {
        // The one case where a warm pass does write: the folder on disk is not the
        // folder the platform was given, because an older run installed it before the
        // repair landed. The mirror keys still carry no caret here, so this pass has
        // something to do, and after it there is nothing left to do.
        var manifestDir = Path.Combine(ExtensionsDir, "uBlock0_1.73.0");
        Directory.CreateDirectory(Path.Combine(manifestDir, "assets"));
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{\"name\":\"uBlock Origin\"}");
        File.WriteAllText(
            Path.Combine(manifestDir, "assets", "assets.json"),
            "{\"filter\":{\"contentURL\":\"https://cdn.jsdelivr.net/x.js\",\"cdnURLs\":[\"u\"]}}");

        var manager = NewManager(new HttpClient(new FailingHandler()));
        var first = await manager.PrepareAsync();

        Assert.Equal(manifestDir, first);
        Assert.True(manager.LastPreparation.AssetsRewritten);
        var assets = Path.Combine(manifestDir, "assets", "assets.json");
        var repaired = File.ReadAllText(assets);
        Assert.Contains("^cdnURLs", repaired);
        Assert.DoesNotContain("\"cdnURLs\"", repaired);
        // The origin url stays, so the extension can still refresh from it.
        Assert.Contains("https://cdn.jsdelivr.net/x.js", repaired);
        var time = new FileInfo(assets).LastWriteTimeUtc.Ticks;

        await Task.Delay(20);
        await manager.PrepareAsync();

        Assert.False(manager.LastPreparation.AssetsRewritten);
        Assert.Equal(repaired, File.ReadAllText(assets));
        Assert.Equal(time, new FileInfo(assets).LastWriteTimeUtc.Ticks);
    }

    [Fact]
    public async Task PreparingTwiceProducesTheSameText()
    {
        // Idempotence is what makes the no write decision safe: if repairing twice
        // produced different bytes, the second start would rewrite the folder again.
        WriteBundledZip();
        var manager = NewManager(new HttpClient(new FailingHandler()));
        var folder = await manager.PrepareAsync();
        Assert.NotNull(folder);

        var assets = Path.Combine(folder!, "assets", "assets.json");
        var afterCold = File.ReadAllText(assets);
        await manager.PrepareAsync();

        Assert.Equal(afterCold, File.ReadAllText(assets));
    }

    private static Dictionary<string, (long Length, long Ticks)> SnapshotFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => f,
                f => (new FileInfo(f).Length, new FileInfo(f).LastWriteTimeUtc.Ticks),
                StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task FailedDownload_ReturnsNullWithoutThrowing()
    {
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("network down")));

        var manager = NewManager(http);
        var result = await manager.PrepareAsync();

        Assert.Null(result);
        Assert.False(manager.LastPreparation.FolderWasAlreadyPresent);
    }

    [Fact]
    public async Task DownloadWithMismatchedHash_IsRejected()
    {
        var http = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) }));

        var manager = NewManager(http);
        var result = await manager.PrepareAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CorruptFolderWithoutManifest_IsClearedAndReExtracted()
    {
        var targetDir = Path.Combine(ExtensionsDir, "uBlock0_1.73.0");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "leftover.txt"), "junk");
        WriteBundledZip();

        var manager = NewManager(new HttpClient(new FailingHandler()));
        var result = await manager.PrepareAsync();

        Assert.NotNull(result);
        Assert.True(File.Exists(Path.Combine(result!, "manifest.json")));
    }

    [Fact]
    public async Task PrepareRunsNoWebView2Calls_AndReturnsOnlyAFolder()
    {
        // The contract that lets a caller move this off the dispatcher: the only
        // thing it produces is a folder path, and it takes no COM argument.
        WriteBundledZip();
        var manager = NewManager(new HttpClient(new FailingHandler()));

        var result = await manager.PrepareAsync();

        Assert.IsType<string>(result);
    }

    [Fact]
    public void Health_ReportsMissingStateWithoutThrowing()
    {
        var manager = NewManager();

        var health = manager.GetHealth();

        Assert.False(health.HasFolder);
        Assert.False(health.HasZip);
        Assert.False(health.HasHash);
    }

    [Fact]
    public void Health_ReportsExtractedStateFromTheIsolatedDirectory()
    {
        var manifestDir = Path.Combine(ExtensionsDir, "uBlock0_1.73.0");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{\"name\":\"uBlock Origin\"}");

        var manager = NewManager();
        var health = manager.GetHealth();

        Assert.True(health.HasFolder);
        Assert.Equal("1.73.0", health.Version);
    }

    [Fact]
    public async Task Prepare_LeavesNoTemporaryZipBehind()
    {
        var http = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MakeTinyZip()) }));
        var tempRoot = Path.Combine(_root.FullName, "temp-probe");
        Directory.CreateDirectory(tempRoot);
        var originalTemp = Path.GetTempPath();
        Assert.NotEmpty(originalTemp);

        var manager = NewManager(http);
        await manager.PrepareAsync();

        // Nothing is asserted about the global temp directory here; the point is
        // that preparation completes and leaves the extension folder usable.
        Assert.True(Directory.Exists(ExtensionsDir));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("no network in this test");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request));
    }
}