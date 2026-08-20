using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace StrideBrowser.Tests.Fixtures;

/// <summary>
/// Scaffold for WebView2-based reader extractor tests.
/// Real implementation needs STA thread, hidden HWND, scoped UserDataFolder,
/// and shared environment via ICollectionFixture. Until that plumbing exists,
/// this is a stub so the scaffold compiles and tests do not pretend to cover DOM heuristics.
/// See ADR v4 test infrastructure notes.
/// </summary>
public sealed class WebView2Fixture : IAsyncLifetime
{
    // Scaffold: no real WebView2 plumbing yet. CreateControllerAsync would need
    // CoreWebView2Environment.CreateAsync with temp UserDataFolder and env.CreateCoreWebView2ControllerAsync(hwnd).

    public Task InitializeAsync()
    {
        // Not implemented. Real version creates hidden host window on STA and shared environment.
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}

[CollectionDefinition("WebView2", DisableParallelization = true)]
public sealed class WebView2Collection : ICollectionFixture<WebView2Fixture> { }
