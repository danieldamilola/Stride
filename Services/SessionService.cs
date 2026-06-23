using System.Diagnostics;
using SpurBrowser.Engine;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

/// <summary>
/// Manages session persistence — saving and restoring tab state,
/// handling command-line URLs, and clearing browsing data on exit.
/// </summary>
public sealed class SessionService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISettingsStore _settingsStore;
    private readonly BrowserSettings _settings;

    public SessionService(ISessionStore sessionStore, ISettingsStore settingsStore, BrowserSettings settings)
    {
        _sessionStore = sessionStore;
        _settingsStore = settingsStore;
        _settings = settings;
    }

    public async Task RestoreOrCreateTabAsync(TabEngine engine)
    {
        var restored = false;
        if (_settings.RestoreSessionOnStartup)
        {
            var session = _sessionStore.Load();
            if (session.Count > 0)
            {
                foreach (var entry in session)
                {
                    var tab = engine.CreateTab(entry.Url);
                    tab.Title = entry.Title;
                    tab.IsPinned = entry.IsPinned;
                }
                engine.SwitchTo(engine.Tabs[0]);
                await engine.ActivateAsync(engine.Tabs[0]);
                restored = true;
            }
        }

        if (!restored)
        {
            var tab = engine.CreateTab();
            engine.SwitchTo(tab);
            await engine.ActivateAsync(tab);
        }
    }

    public async Task HandleCommandLineUrlsAsync(TabEngine engine)
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args.Skip(1))
        {
            if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var tab = engine.CreateTab(arg);
                engine.SwitchTo(tab);
                await engine.ActivateAsync(tab);
                break;
            }
        }
    }

    public async Task SaveAndCleanupAsync(TabEngine engine)
    {
        try
        {
            if (_settings.RestoreSessionOnStartup)
            {
                var tabs = engine.Tabs
                    .Where(t => !InternalUrls.IsInternal(t.Url))
                    .Select(t => (t.Url, t.Title, t.IsPinned));
                _sessionStore.Save(tabs);
            }

            if (_settings.ClearDataOnExit)
            {
                try
                {
                    var profile = engine.GetCoreWebView2()?.Profile;
                    if (profile is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await profile.ClearBrowsingDataAsync().WaitAsync(cts.Token);
                    }
                }
                catch { /* Timeout or disposal — best-effort cleanup */ }
            }

            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SessionService.SaveAndCleanupAsync failed: {ex}");
        }
    }
}
