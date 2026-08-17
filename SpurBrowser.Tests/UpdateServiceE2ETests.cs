using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Chaos.NaCl;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// End-to-end dry run of the real update pipeline (real UpdateService + real NetSparkle +
/// real HTTP + real Ed25519 verification) against a local appcast and a throwaway key.
/// Proves: (1) the appcast check works with NO .signature sidecar (OnlyVerifySoftwareDownloads),
/// (2) a tampered installer is rejected before anything runs, (3) a correctly signed installer
/// reaches the install step. The dummy installer is a WinExe that exits immediately — no real
/// install ever happens.
/// </summary>
public sealed class UpdateServiceE2ETests : IDisposable
{
    private enum Route { Valid, Tampered }

    private static readonly byte[] InstallerBytes = EnsureDummyInstaller();
    private static readonly byte[] TamperedBytes = FlipByte(InstallerBytes);

    private readonly HttpListener _server;
    private readonly string _baseUrl;
    private readonly string _installerName;
    private readonly string _appcastXml;
    private readonly string _fixtureId = Guid.NewGuid().ToString("N")[..6];
    private readonly System.Diagnostics.TextWriterTraceListener _traceRelay;
    private volatile Route _route = Route.Valid;

    public UpdateServiceE2ETests()
    {
        _traceRelay = new System.Diagnostics.TextWriterTraceListener(Console.Out) { Filter = new System.Diagnostics.EventTypeFilter(System.Diagnostics.SourceLevels.All) };
        System.Diagnostics.Trace.Listeners.Add(_traceRelay);

        var seed = Convert.FromBase64String(Ed25519.GeneratePrivateKeySeed());
        var publicKey = Ed25519.PublicKeyFromSeed(seed);
        var signature = Ed25519.Sign(InstallerBytes, Ed25519.ExpandedPrivateKeyFromSeed(seed));

        // Unique installer file name per run: NetSparkle names the temp download after the URL,
        // and a stale %TEMP% file from a previous (differently-keyed) run would poison the check.
        _installerName = $"installer-{Guid.NewGuid():N}.exe";

        var port = GetFreePort();
        _server = new HttpListener();
        _server.Prefixes.Add($"http://127.0.0.1:{port}/");
        _server.Start();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _ = ServeAsync();

        _appcastXml = BuildAppcast(_baseUrl, _installerName, signature);

        Environment.SetEnvironmentVariable("STRIDE_APPCAST_URL", _baseUrl + "appcast.xml");
        Environment.SetEnvironmentVariable("STRIDE_UPDATE_PUBLIC_KEY", Convert.ToBase64String(publicKey));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STRIDE_APPCAST_URL", null);
        Environment.SetEnvironmentVariable("STRIDE_UPDATE_PUBLIC_KEY", null);
        System.Diagnostics.Trace.Listeners.Remove(_traceRelay);
        _traceRelay.Dispose();
        _server.Stop();
    }

    [Fact]
    public async Task CheckForUpdates_AppcastWithoutSidecar_ReturnsItem()
    {
        var service = new UpdateService();
        var item = await service.CheckForUpdateCustomAsync();
        Assert.NotNull(item);
        Assert.Equal("99.0.0", item!.Version);
    }

    [Fact]
    public async Task DownloadAndInstall_TamperedInstaller_RejectedBeforeInstall()
    {
        _route = Route.Tampered;
        var service = new UpdateService();

        var exitRequested = new TaskCompletionSource<object?>();
        service.AppExitRequested += () => exitRequested.TrySetResult(null);

        // Transient Windows file-lock jitter can abort the first attempt with a delete error;
        // the flow is idempotent, so retry a few times.
        string message;
        while (true)
        {
            var failed = new TaskCompletionSource<string>();
            service.UpdateFailed += (_, msg) => failed.TrySetResult(msg);
            Assert.True(await service.DownloadAndInstallUpdateAsync());
            message = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (!IsTransientLockFailure(message))
                break;
            await Task.Delay(500);
        }

        Assert.True(message.Contains("signature", StringComparison.OrdinalIgnoreCase), $"unexpected failure message: {message}");
        Assert.False(exitRequested.Task.IsCompleted, "install step must not be reached for a tampered installer");
    }

    [Fact]
    public async Task DownloadAndInstall_ValidInstaller_ReachesInstallStep()
    {
        var service = new UpdateService();

        var exitRequested = new TaskCompletionSource<object?>();
        service.AppExitRequested += () => exitRequested.TrySetResult(null);

        while (true)
        {
            var failed = new TaskCompletionSource<string>();
            service.UpdateFailed += (_, msg) => failed.TrySetResult(msg);
            Assert.True(await service.DownloadAndInstallUpdateAsync());
            var winner = await Task.WhenAny(exitRequested.Task, failed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (winner == exitRequested.Task)
                return;
            if (winner == failed.Task && IsTransientLockFailure(failed.Task.Result))
            {
                await Task.Delay(500);
                continue;
            }
            Assert.Fail($"unexpected failure on valid path: {(winner == failed.Task ? failed.Task.Result : "timed out waiting for install")}");
        }
    }

    private async Task ServeAsync()
    {
        while (_server.IsListening)
        {
            try
            {
                var ctx = await _server.GetContextAsync();
                var path = ctx.Request.Url!.AbsolutePath;
                var isHead = ctx.Request.HttpMethod == "HEAD";
                Console.WriteLine($"[e2e-server:{_fixtureId}] {ctx.Request.HttpMethod} {path} (route={_route})");
                switch (path)
                {
                    case "/appcast.xml":
                        ctx.Response.ContentType = "application/xml";
                        if (!isHead)
                        {
                            var body = Encoding.UTF8.GetBytes(_appcastXml);
                            ctx.Response.ContentLength64 = body.Length;
                            await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length);
                        }
                        break;
                    case string p when p == "/" + _installerName:
                        ctx.Response.ContentType = "application/octet-stream";
                        if (!isHead)
                        {
                            var payload = _route == Route.Tampered ? TamperedBytes : InstallerBytes;
                            ctx.Response.ContentLength64 = payload.Length;
                            await ctx.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        }
                        break;
                    default:
                        ctx.Response.StatusCode = 404;
                        break;
                }
                ctx.Response.Close();
            }
            catch (HttpListenerException)
            {
                if (!_server.IsListening) return;
            }
        }
    }

    private static string BuildAppcast(string baseUrl, string installerName, byte[] signature)
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
              <channel>
                <title>Stride E2E Test</title>
                <item>
                  <title>Version 99.0.0</title>
                  <sparkle:releaseNotesLink>{baseUrl}notes.md</sparkle:releaseNotesLink>
                  <pubDate>{DateTime.UtcNow:ddd, dd MMM yyyy HH:mm:ss} UTC</pubDate>
                  <enclosure url="{baseUrl}{installerName}" sparkle:version="99.0.0" sparkle:os="windows" length="{InstallerBytes.Length}" type="application/octet-stream" sparkle:edSignature="{Convert.ToBase64String(signature)}" />
                </item>
              </channel>
            </rss>
            """;
    }

    private static bool IsTransientLockFailure(string message) =>
        message.Contains("Unable to delete", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);

    private static byte[] FlipByte(byte[] source)
    {
        var copy = (byte[])source.Clone();
        copy[copy.Length / 2] ^= 0xFF;
        return copy;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Compiles (once, cached) a tiny WinExe that exits immediately — the "installer" that the
    /// pipeline may legally run. WinExe = no console window flash.
    /// </summary>
    private static byte[] EnsureDummyInstaller()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stride-e2e", "dummy-installer");
        var exe = Path.Combine(dir, "bin", "Release", "net9.0", "DummyInstaller.exe");
        if (File.Exists(exe))
            return File.ReadAllBytes(exe);

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "DummyInstaller.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>""");
        File.WriteAllText(Path.Combine(dir, "Program.cs"),
            "using System;\n" +
            "Console.WriteLine(\"Stride E2E dummy installer ran.\");\n" +
            "Environment.Exit(0);\n");

        var psi = new ProcessStartInfo("dotnet", $"build \"{dir}\" -c Release --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(120_000);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException("Failed to compile the E2E dummy installer:\n" + proc.StandardError.ReadToEnd());

        return File.ReadAllBytes(exe);
    }
}