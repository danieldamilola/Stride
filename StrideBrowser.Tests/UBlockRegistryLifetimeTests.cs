using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Executes the pinned uBlock Origin 1.73.0 registry loader across two background
/// lifetimes over one shared cache, and checks what each lifetime did.
///
/// Every other test in this area reads the repaired script. That cannot show whether
/// the repaired script works. Moving the storage key on the get and the set looks like
/// a finished repair and is not: the loader pulls the saved registry back out of the
/// bin the get returned, looking for the upstream property name, and if that name did
/// not move with the key it finds nothing, falls through to the bundled assets.json and
/// rebuilds, on every single start, throwing away whatever the previous start saved.
///
/// So these tests write the repaired script to disk and run the shipped function over
/// it with node. The functions are extracted from the pinned script by anchor and
/// executed, not paraphrased. The three claims under test are the three a file
/// comparison cannot make: the old cache is missed and rebuilt once, the second
/// lifetime comes back out of the cache with newer metadata intact, and a script with
/// the callback reverted fails both the check and the two lifetimes.
/// </summary>
public sealed class UBlockRegistryLifetimeTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("stride-registry-lifetime").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); }
        catch (Exception) { /* a temp directory is not worth failing a run over */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheRepairedScript_RebuildsTheOldCachedRegistryAndSavesItUnderTheRepairedKey()
    {
        // The state a profile already has: a cached registry under the upstream key,
        // built from an assets.json whose entries carried no remote urls. The repaired
        // loader must not read it, so the first lifetime is a miss, rebuilds from the
        // repaired file, and persists under the key it will read next time.
        var run = Observe(RepairedScript(PinnedScript()), "repaired");

        Assert.True(run.Exit == 0, run.Detail);
        Assert.True(run.Report.GetKeys.All(k => k == UBlockAssetRepair.RepairedRegistryKey),
            $"only the repaired key may be read, read: {string.Join(", ", run.Report.GetKeys)}");
        Assert.Contains(UBlockAssetRepair.RepairedRegistryKey, run.Report.SetKeys);
        Assert.DoesNotContain("assetSourceRegistry", run.Report.SetKeys);
        Assert.True(run.Report.FirstRebuiltFromBundledFile,
            "an old cached registry under the upstream key has to be missed, not reused");
        Assert.True(run.Report.FirstEntryHasRemoteUrl,
            "the rebuild has to come from the repaired file, so the entry keeps a remote url");
    }

    [Fact]
    public void TheNextBackgroundLifetime_ReadsTheCachedRegistryBackAndKeepsTheNewerMetadata()
    {
        // The claim the old repair could not make. A cache the previous start saved is
        // read back, and the metadata that start learned, which the bundled file knows
        // nothing about, survives. No fallback, no rebuild, no second read of the
        // bundled file.
        var run = Observe(RepairedScript(PinnedScript()), "stale");

        Assert.True(run.Exit == 0, run.Detail);
        Assert.False(run.Report.SecondRebuiltFromBundledFile,
            "the second lifetime read the bundled file, so the saved registry was not used");
        Assert.Equal(0, run.Report.FetchTextCalls);
        Assert.True(run.Report.SecondEntryHasRemoteUrl);
        Assert.True(run.Report.SecondKeptNewerRemoteUrl,
            "the remote url only the saved registry carried was dropped on the next start");
        Assert.True(run.Report.SecondKeptNewerUpdateAfter,
            "the update schedule only the saved registry carried was dropped on the next start");
        Assert.False(run.Report.UpstreamKeyStillWritten);
    }

    [Fact]
    public void AScriptWithTheCallbackReverted_IsNotRepaired_AndLosesTheSavedRegistry()
    {
        // The deliberate reversion. Take the repaired script and put the upstream
        // property name back on the two callback reads, which is what a change that
        // only moved the storage key looks like. It must fail the check, and it must
        // fail in the runtime too, so this test is not passing because the harness
        // cannot tell the difference.
        var reverted = RevertCallback(RepairedScript(PinnedScript()));

        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(reverted));
        var reason = UBlockAssetRepair.RegistryKeyNotRepairedReason(reverted);
        Assert.NotNull(reason);
        Assert.Contains("saved-registry", reason);

        var run = Observe(reverted, "stale");

        Assert.NotEqual(0, run.Exit);
        Assert.False(run.Report.SecondKeptNewerRemoteUrl,
            "a reverted callback reads nothing back, so the harness has to see it");
        Assert.False(run.Report.SecondKeptNewerUpdateAfter);
    }

    [Fact]
    public void AScriptThatMovedOnlyTheStorageKey_IsFinishedByTheRepair()
    {
        // A folder an earlier build touched once has the read and the write on the new
        // key and the callback still on the old property. That is the worst state,
        // because it reads a name the callback cannot find and rebuilds every start.
        var half = MoveStorageKeyOnly(PinnedScript());

        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(half));
        Assert.Contains("saved-registry", UBlockAssetRepair.RegistryKeyNotRepairedReason(half));

        var finished = UBlockAssetRepair.RepairRegistryKey(half);
        Assert.Equal(UBlockAssetRepair.RepairOutcome.Rewritten, finished.Outcome);
        Assert.True(UBlockAssetRepair.RegistryKeyIsRepaired(finished.RepairedText!));

        // And the finished form actually holds across the next start.
        var run = Observe(finished.RepairedText!, "stale");
        Assert.True(run.Exit == 0, run.Detail);
        Assert.True(run.Report.SecondKeptNewerRemoteUrl);
        Assert.Equal(0, run.Report.FetchTextCalls);
    }

    [Fact]
    public void AScriptWithNoPinnedRegistryAccess_IsReportedNotPossibleAndLeftAlone()
    {
        // A different uBlock version, or a script that was already edited by something
        // else. Neither is patched on a guess, and neither is reported as repaired.
        const string foreign =
            "const store = new Map();\n" +
            "async function getAssetSourceRegistry() {\n" +
            "    return store.get('registry');\n" +
            "}\n";

        var result = UBlockAssetRepair.RepairRegistryKey(foreign);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Null(result.RepairedText);
        Assert.False(UBlockAssetRepair.RegistryKeyIsRepaired(foreign));
    }

    [Fact]
    public void AMissingScript_IsReportedNotPossibleRatherThanAssumedHealthy()
    {
        var result = UBlockAssetRepair.RepairRegistryKey(null);

        Assert.Equal(UBlockAssetRepair.RepairOutcome.NotPossible, result.Outcome);
        Assert.Null(result.RepairedText);
        Assert.Equal("there is no js/assets.js in this folder",
            UBlockAssetRepair.RegistryKeyNotRepairedReason(null));
    }

    // ---------------------------------------------------------------- the fixtures

    private static string ShippedZip()
    {
        const string name = "uBlock0_1.73.0.chromium.zip";
        var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions", name);
        if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException(
            $"the shipped uBlock zip is not beside the test binary: {candidate}", name);
    }

    private static string ZipEntry(string suffix)
    {
        using var zip = ZipFile.OpenRead(ShippedZip());
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(suffix, StringComparison.Ordinal)) continue;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        throw new InvalidOperationException($"the pinned package carries no {suffix}");
    }

    private static string PinnedScript() => ZipEntry("js/assets.js");

    private static string RepairedScript(string pinned) =>
        UBlockAssetRepair.RepairRegistryKey(pinned).RepairedText
        ?? throw new InvalidOperationException(
            "the pinned script was not recognised as the one this repair matches");

    /// <summary>The upstream property name put back on both callback reads.</summary>
    private static string RevertCallback(string repaired) =>
        repaired.Replace(
            $"bin['{UBlockAssetRepair.RepairedRegistryKey}']",
            "bin.assetSourceRegistry");

    /// <summary>Only the get and the set moved, which is the incomplete repair itself.</summary>
    private static string MoveStorageKeyOnly(string pinned) => PinnedScriptForm.StorageKeyOnly(pinned);

    // ---------------------------------------------------------------- the two lifetimes

    private sealed record LifetimeReport(
        string[] GetKeys,
        string[] SetKeys,
        int FetchTextCalls,
        bool FirstRebuiltFromBundledFile,
        bool SecondRebuiltFromBundledFile,
        bool FirstEntryHasRemoteUrl,
        bool SecondEntryHasRemoteUrl,
        bool SecondKeptNewerRemoteUrl,
        bool SecondKeptNewerUpdateAfter,
        bool UpstreamKeyStillWritten);

    private sealed record RunResult(int Exit, LifetimeReport Report, string Detail);

    /// <summary>
    /// Runs the script under test over two background lifetimes sharing one cache and
    /// returns what each lifetime did. The harness is the pinned loader itself, so a
    /// verdict here is the extension's behaviour rather than this test's reading of it.
    /// A failing run is returned rather than thrown, because one of these tests is
    /// about what a reversion does.
    /// </summary>
    private RunResult Observe(string scriptUnderTest, string mode)
    {
        var scriptPath = Path.Combine(_work, $"assets-{mode}-{Guid.NewGuid():N}.js");
        File.WriteAllText(scriptPath, scriptUnderTest);
        var assetsJsonPath = Path.Combine(_work, $"assets-{Guid.NewGuid():N}.json");
        File.WriteAllText(assetsJsonPath, ZipEntry("assets/assets.json"));

        var harness = Path.Combine(RepoRoot(), "StrideBrowser.Tests", "ublock-registry-lifetime.mjs");
        Assert.True(File.Exists(harness), $"the lifetime harness is missing: {harness}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exit = RunNode(harness, scriptPath, assetsJsonPath, mode, stdout, stderr);

        var raw = stdout.ToString();
        Assert.True(raw.TrimStart().StartsWith('{'),
            $"the lifetime harness produced no report for {mode}.\nstdout: {raw}\nstderr: {stderr}");

        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        var report = new LifetimeReport(
            Strings(root, "getKeys"),
            Strings(root, "setKeys"),
            root.GetProperty("fetchTextCalls").GetArrayLength(),
            root.GetProperty("firstRebuiltFromBundledFile").GetBoolean(),
            root.GetProperty("secondRebuiltFromBundledFile").GetBoolean(),
            root.GetProperty("firstEntryHasRemoteUrl").GetBoolean(),
            root.GetProperty("secondEntryHasRemoteUrl").GetBoolean(),
            root.GetProperty("secondKeptNewerRemoteUrl").GetBoolean(),
            root.GetProperty("secondKeptNewerUpdateAfter").GetBoolean(),
            root.GetProperty("upstreamKeyStillWritten").GetBoolean());

        return new RunResult(exit, report, $"exit {exit}\nstdout: {raw}\nstderr: {stderr}");
    }

    private static string[] Strings(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(v => v.GetString()!).ToArray();

    private static int RunNode(
        string script, string scriptPath, string assetsJsonPath, string mode,
        StringBuilder stdout, StringBuilder stderr)
    {
        var start = new ProcessStartInfo(ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { script, scriptPath, assetsJsonPath, mode })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"node did not start: {start.FileName}");
        stdout.Append(process.StandardOutput.ReadToEnd());
        stderr.Append(process.StandardError.ReadToEnd());
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string ResolveNode()
    {
        var probe = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        probe.ArgumentList.Add("--version");
        try
        {
            using var p = Process.Start(probe);
            if (p is not null)
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0) return "node";
            }
        }
        catch (Exception)
        {
            // Fall through to the installed location below.
        }

        foreach (var candidate in new[]
                 {
                     @"C:\Program Files\nodejs\node.exe",
                     @"C:\Program Files (x86)\nodejs\node.exe"
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            "node is needed to run the pinned uBlock registry loader over two background " +
            "lifetimes. On this machine it is at C:\\Program Files\\nodejs\\node.exe.");
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