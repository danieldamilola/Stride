using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Runs the exact text YouTubeUnhook generates against a stubbed YouTube document, over
/// a page that stores the config and over one that refuses it.
///
/// The generated script writes the config to localStorage and the unhook script reads
/// window.__STRIDE_UNHOOK first and localStorage second, returning an empty object when
/// both come back nothing. So a page that denies storage, which a browser does for
/// blocked third party storage and for a private window, had nothing to read. Every
/// setting then read as off, no element was hidden, and nothing reported the reason. The
/// generator, the script and the order they run in decide that, so it is measured by
/// running them rather than by reading the generated text.
///
/// The harness is youtube-unhook-storage.mjs next to this file. It reports the classes
/// that reached <html> after the first injection and again after a live settings change
/// followed by a yt-navigate-finish event, so a setting that applies on load but not
/// after the page navigates is visible.
/// </summary>
public sealed class YouTubeUnhookStorageDenialTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("stride-unhook-storage").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); }
        catch (Exception) { /* a temp directory is not worth failing a run over */ }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSettingsReachThePage_WhetherOrNotThePageLetsThemBeStored(bool denyStorage)
    {
        // The shipped defaults hide the sidebar, the comments and Shorts, so those three
        // are what a working unhook has to switch on.
        var run = Observe(new BrowserSettings(), denyStorage);

        Assert.Equal(denyStorage ? "denied" : "allowed", run.Report.Mode);
        Assert.Equal(2, run.Report.StorageWritesRefused.Length + run.Report.StorageWrites.Length);
        Assert.Equal(denyStorage ? 0 : 2, run.Report.StorageWrites.Length);
        Assert.Contains("stride-unhook-sidebar", run.Report.AfterLoad);
        Assert.Contains("stride-unhook-comments", run.Report.AfterLoad);
        Assert.Contains("stride-unhook-shorts", run.Report.AfterLoad);
    }

    [Fact]
    public void TheRunningDocumentIsOnlyPointedAtTheConfigWhenThePageRefusedTheWrite()
    {
        // Pointing at the config on the happy path as well would make persistence
        // untestable: a page that stores nothing and a page whose stored value was ignored
        // would then look the same to everything downstream.
        var allowed = Observe(new BrowserSettings(), denyStorage: false);
        var denied = Observe(new BrowserSettings(), denyStorage: true);

        Assert.False(allowed.Report.LiveConfigAfterLoad);
        Assert.True(denied.Report.LiveConfigAfterLoad);

        // A live settings change points at the config either way.
        Assert.True(allowed.Report.LiveConfigAfterLiveChange);
        Assert.True(denied.Report.LiveConfigAfterLiveChange);

        Assert.Equal(allowed.Report.AfterLoad, denied.Report.AfterLoad);
    }

    [Fact]
    public void ALiveSettingsChangeStillAppliesAfterThePageRefusedStorage()
    {
        // A live change arrives as a second injection and only takes hold when YouTube
        // reports a navigation, so the classes after the event are the ones that matter.
        var run = Observe(
            new BrowserSettings { UnhookHideComments = false, UnhookHideShorts = false },
            denyStorage: true);

        Assert.Contains("stride-unhook-sidebar", run.Report.AfterLiveChange);
        Assert.DoesNotContain("stride-unhook-comments", run.Report.AfterLiveChange);
        Assert.DoesNotContain("stride-unhook-shorts", run.Report.AfterLiveChange);
    }

    private sealed record StorageReport(
        string Mode,
        string[] StorageReads,
        string[] StorageWrites,
        string[] StorageWritesRefused,
        bool LiveConfigAfterLoad,
        bool LiveConfigAfterLiveChange,
        JsonElement ResolvedConfig,
        string[] AfterLoad,
        string[] AfterLiveChange);

    private sealed record RunResult(int Exit, StorageReport Report, string Detail);

    private RunResult Observe(BrowserSettings liveSettings, bool denyStorage)
    {
        var mode = denyStorage ? "denied" : "allowed";
        var unhook = new YouTubeUnhook();
        var initial = Path.Combine(_work, $"initial-{mode}.js");
        var live = Path.Combine(_work, $"live-{mode}.js");
        File.WriteAllText(initial, unhook.GetScript(new BrowserSettings()));
        File.WriteAllText(live, unhook.GetScript(liveSettings, forceConfig: true));

        var harness = Path.Combine(RepoRoot(), "StrideBrowser.Tests", "youtube-unhook-storage.mjs");
        Assert.True(File.Exists(harness), $"the storage harness is missing: {harness}");

        var start = new ProcessStartInfo(ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { harness, initial, live, mode })
            start.ArgumentList.Add(arg);

        var run = OwnedProcess.Run(start, TimeSpan.FromSeconds(60));
        Assert.False(run.TimedOut, run.Detail);
        Assert.True(run.Collected, run.Detail);
        Assert.True(run.ExitCode == 0, run.Detail);

        var raw = run.Stdout;
        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        return new RunResult(run.ExitCode, new StorageReport(
            root.GetProperty("mode").GetString()!,
            Strings(root, "storageReads"),
            Strings(root, "storageWrites"),
            Strings(root, "storageWritesRefused"),
            root.GetProperty("liveConfigAfterLoad").GetBoolean(),
            root.GetProperty("liveConfigAfterLiveChange").GetBoolean(),
            root.GetProperty("resolvedConfig").Clone(),
            Strings(root, "afterLoad"),
            Strings(root, "afterLiveChange")), run.Detail);
    }

    private static string[] Strings(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(v => v.GetString()!).ToArray();

    internal static string ResolveNode()
    {
        foreach (var candidate in new[]
                 {
                     "node",
                     @"C:\Program Files\nodejs\node.exe",
                     @"C:\Program Files (x86)\nodejs\node.exe"
                 })
        {
            var probe = new ProcessStartInfo(candidate)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            probe.ArgumentList.Add("--version");
            try
            {
                var run = OwnedProcess.Run(probe, TimeSpan.FromSeconds(5));
                if (!run.TimedOut && run.ExitCode == 0) return candidate;
            }
            catch (Exception)
            {
                // Fall through to the installed location below.
            }
        }

        throw new FileNotFoundException(
            "node is needed to run the generated unhook script against a page that " +
            "refuses storage. On this machine it is at C:\\Program Files\\nodejs\\node.exe.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stride.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
