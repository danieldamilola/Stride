using System.Diagnostics;

namespace SpurBrowser.Engine;

/// <summary>
/// Runs a multi-phase stress test against the tab engine to verify stability
/// under rapid tab creation, switching, closing, and restoration.
/// </summary>
public sealed class StressTestRunner
{
    private readonly TabEngine _engine;

    public StressTestRunner(TabEngine engine)
    {
        _engine = engine;
    }

    public async Task RunAsync()
    {
        var sites = new[]
        {
            "https://www.google.com",
            "https://www.github.com",
            "https://www.wikipedia.org",
            "https://www.reddit.com",
            "https://www.stackoverflow.com",
            "https://www.youtube.com",
            "https://www.amazon.com",
            "https://news.ycombinator.com",
            "https://www.bbc.com",
            "https://www.nytimes.com",
            "https://www.twitch.tv",
            "https://www.microsoft.com",
            "https://www.apple.com",
            "https://www.linkedin.com",
            "https://www.twitter.com"
        };

        Trace.WriteLine("=== STRESS TEST: Starting ===");
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        var createdTabs = new List<Models.BrowserTab>();

        // Phase 1: Rapid tab creation
        Trace.WriteLine("Phase 1: Opening 15 tabs...");
        if (_engine.ActiveTab is not null) _engine.ActiveTab.Title = "[Stress] Opening tabs...";

        foreach (var site in sites)
        {
            try
            {
                var tab = _engine.CreateTab(site);
                createdTabs.Add(tab);
                await _engine.ActivateAsync(tab);
                Trace.WriteLine($"  Opened: {site} ({sw.ElapsedMilliseconds}ms)");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to open {site}: {ex.Message}");
                Trace.WriteLine($"  ERROR: {site} - {ex.Message}");
            }
        }

        Trace.WriteLine($"Phase 1 complete: {createdTabs.Count}/{sites.Length} tabs ({sw.ElapsedMilliseconds}ms)");

        // Phase 2: Cycle through all tabs
        Trace.WriteLine("Phase 2: Cycling through tabs...");
        if (_engine.ActiveTab is not null) _engine.ActiveTab.Title = "[Stress] Cycling tabs...";

        foreach (var tab in createdTabs.ToList())
        {
            try
            {
                _engine.SwitchTo(tab);
                await _engine.ActivateAsync(tab);
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to switch to {tab.Title}: {ex.Message}");
            }
        }

        Trace.WriteLine($"Phase 2 complete ({sw.ElapsedMilliseconds}ms)");

        // Phase 3: Close half the tabs
        Trace.WriteLine("Phase 3: Closing tabs under load...");
        if (_engine.ActiveTab is not null) _engine.ActiveTab.Title = "[Stress] Closing tabs...";

        var toClose = createdTabs.Take(createdTabs.Count / 2).ToList();
        foreach (var tab in toClose)
        {
            try
            {
                _engine.CloseTab(tab);
                createdTabs.Remove(tab);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to close {tab.Title}: {ex.Message}");
            }
        }

        Trace.WriteLine($"Phase 3 complete: closed {toClose.Count} tabs ({sw.ElapsedMilliseconds}ms)");

        // Phase 4: Reopen tabs
        Trace.WriteLine("Phase 4: Reopening closed tabs...");
        if (_engine.ActiveTab is not null) _engine.ActiveTab.Title = "[Stress] Reopening...";

        for (int i = 0; i < toClose.Count; i++)
        {
            try
            {
                var restored = _engine.RestoreClosedTab();
                if (restored is not null)
                {
                    await _engine.ActivateAsync(restored);
                    createdTabs.Add(restored);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to restore tab: {ex.Message}");
            }
        }

        Trace.WriteLine($"Phase 4 complete ({sw.ElapsedMilliseconds}ms)");

        // Phase 5: Cleanup
        Trace.WriteLine("Phase 5: Cleanup...");
        if (_engine.ActiveTab is not null) _engine.ActiveTab.Title = "[Stress] Cleaning up...";

        foreach (var tab in createdTabs.ToList())
        {
            try { _engine.CloseTab(tab); }
            catch (Exception ex) { errors.Add($"Cleanup close failed: {ex.Message}"); }
        }

        sw.Stop();

        var result = errors.Count == 0 ? "PASSED" : $"FAILED ({errors.Count} errors)";
        Trace.WriteLine($"=== STRESS TEST: {result} in {sw.ElapsedMilliseconds}ms ===");
        foreach (var err in errors)
            Trace.WriteLine($"  Error: {err}");

        if (_engine.ActiveTab is not null)
            _engine.ActiveTab.Title = $"Stress Test {result} - {sw.ElapsedMilliseconds}ms";

        using var proc = Process.GetCurrentProcess();
        Trace.WriteLine($"Memory: {proc.WorkingSet64 / 1024 / 1024}MB");
    }
}
