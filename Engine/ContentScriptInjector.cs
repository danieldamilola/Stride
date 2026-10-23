using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Helpers;
using UmberBrowser.Models;
using UmberBrowser.Services;

namespace UmberBrowser.Engine;

public sealed class ContentScriptInjector
{
    private readonly YouTubeUnhook _youtubeUnhook;

    /// <summary>
    /// The settings singleton, when the container supplied one. The injector reads
    /// the adaptive tint setting from it so a change reaches the pages without the
    /// window or the engine having to pass the change along.
    /// </summary>
    private readonly BrowserSettings? _settings;

    private Dispatcher? _dispatcher;

    /// <summary>
    /// The id WebView2 gave the registered YouTube bootstrap on each core.
    ///
    /// AddScriptToExecuteOnDocumentCreatedAsync freezes the string it is handed. The
    /// settings in that string are the ones from the moment the control was built, so
    /// without a record of the id there is no way to replace it when the user changes
    /// a setting, and no way to take it away when the feature is switched off. The
    /// entry dies with the core, so a closed or discarded tab leaves nothing behind,
    /// and a tab that moves between windows keeps the same core and the same record.
    /// </summary>
    private readonly ConditionalWeakTable<CoreWebView2, StrongBox<string?>> _youTubeScriptIds = new();

    /// <summary>
    /// What is registered on one core right now: the id of the theme script and the
    /// setting it was registered with. The record dies with the core, so a closed
    /// tab leaves nothing behind and a tab that moves between windows keeps its
    /// registration.
    /// </summary>
    internal sealed class ThemeScriptState
    {
        public string? ScriptId;
        public bool Enabled;
    }

    /// <summary>
    /// The WebView2 calls the theme script needs. The interface exists so the update
    /// path can be exercised without a live core.
    /// </summary>
    internal interface IThemeScriptTarget
    {
        Task<string> AddScriptAsync(string script);
        void RemoveScript(string scriptId);
        void PostMessage(string message);
    }

    private sealed class CoreThemeScriptTarget(CoreWebView2 core) : IThemeScriptTarget
    {
        public Task<string> AddScriptAsync(string script) => core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        public void RemoveScript(string scriptId) => core.RemoveScriptToExecuteOnDocumentCreated(scriptId);
        public void PostMessage(string message) => core.PostWebMessageAsString(message);
    }

    /// <summary>The per core registration of the theme script.</summary>
    private readonly ConditionalWeakTable<CoreWebView2, ThemeScriptState> _themeStates = new();

    /// <summary>
    /// The cores the theme script was registered on. The references are weak, so a
    /// closed tab is collected and the list does not keep a torn down core alive.
    /// The list is what makes the cores reachable at all: a weak table cannot be
    /// walked, and the setting has to reach every core that is open.
    /// </summary>
    private readonly List<WeakReference<CoreWebView2>> _themeCores = new();

    private readonly object _themeLock = new();

    /// <summary>
    /// Held while a registration is being replaced, so two changes that overlap
    /// cannot leave a core with two scripts or with the older one.
    /// </summary>
    private readonly SemaphoreSlim _themeGate = new(1, 1);

    /// <summary>The control message that turns the adaptive tint on in a live page.</summary>
    internal const string ThemeControlEnabled = "theme-tint-control:1";

    /// <summary>The control message that turns the adaptive tint off in a live page.</summary>
    internal const string ThemeControlDisabled = "theme-tint-control:0";

    /// <summary>
    /// Replaced with the adaptive tint setting when the script is registered, so a
    /// document knows whether it should watch the page before the first page event
    /// arrives.
    /// </summary>
    private const string ThemeEnabledPlaceholder = "__UMBER_THEME_TINT_ENABLED__";

    private static readonly HashSet<string> _trustedExternalOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "wallhaven.cc",
        "www.wallhaven.cc",
    };

    public static bool IsTrustedExternalOrigin(string host) =>
        !string.IsNullOrEmpty(host) && _trustedExternalOrigins.Contains(host);

    public ContentScriptInjector(YouTubeUnhook youtubeUnhook, BrowserSettings? settings = null)
    {
        _youtubeUnhook = youtubeUnhook;
        _settings = settings;
        _dispatcher = Application.Current?.Dispatcher;

        // The settings singleton is shared by every window, so one subscription here
        // reaches the pages of all of them, including a tab that moved to another
        // window. The subscriber is the injector itself, which lives as long as the
        // settings do.
        if (settings is not null)
            settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>
    /// The theme colour script as it is registered on a core.
    /// <para>
    /// The setting is baked into the text. A document that is created later then
    /// starts in the right state without asking the host, and a document that was
    /// created while the setting was off is told to start through a control message.
    /// </para>
    /// </summary>
    internal static string BuildThemeScript(bool enabled)
    {
        var script = ResourceLoader.Load("Resources.Scripts.theme-color.js");
        return script.Replace(ThemeEnabledPlaceholder, enabled ? "true" : "false");
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserSettings.AdaptiveToolbarTint)) return;
        _ = ApplyAdaptiveTintAsync();
    }

    /// <summary>
    /// Brings every live core in step with the adaptive tint setting. The registered
    /// script is replaced with one built for the setting as it is now, so the next
    /// document starts in the right state, and the document that is open now is told
    /// to start or stop watching the page.
    /// </summary>
    public async Task ApplyAdaptiveTintAsync()
    {
        try
        {
            var dispatcher = UiDispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                Func<Task> work = () => ApplyAdaptiveTintOnCurrentThreadAsync();
                var operation = dispatcher.InvokeAsync(work);
                await await operation;
                return;
            }

            await ApplyAdaptiveTintOnCurrentThreadAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ContentScriptInjector: adaptive tint update failed: {ex.Message}");
        }
    }

    private async Task ApplyAdaptiveTintOnCurrentThreadAsync()
    {
        foreach (var core in LiveThemeCores())
        {
            try
            {
                await ApplyThemeStateAsync(
                    new CoreThemeScriptTarget(core),
                    ThemeState(core),
                    () => DesiredEnabled(null));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ContentScriptInjector: adaptive tint update failed for a core: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Registers the script for the current setting on one target and tells the
    /// document that is open now. The setting is read while the update is held, so
    /// the newest change wins when two of them overlap, and the document is never
    /// told to do something the registered script does not say.
    /// </summary>
    internal async Task ApplyThemeStateAsync(IThemeScriptTarget target, ThemeScriptState state, Func<bool> desiredState)
    {
        var enabled = false;
        await _themeGate.WaitAsync();
        try
        {
            enabled = desiredState();
            if (state.Enabled != enabled || string.IsNullOrEmpty(state.ScriptId))
            {
                if (!string.IsNullOrEmpty(state.ScriptId))
                    target.RemoveScript(state.ScriptId);
                state.ScriptId = await target.AddScriptAsync(BuildThemeScript(enabled));
                state.Enabled = enabled;
            }
        }
        finally
        {
            _themeGate.Release();
        }

        target.PostMessage(enabled ? ThemeControlEnabled : ThemeControlDisabled);
    }

    private async Task RegisterThemeScriptAsync(CoreWebView2 core, BrowserSettings settings)
    {
        try
        {
            lock (_themeLock)
            {
                _themeCores.RemoveAll(entry => !entry.TryGetTarget(out _));
                var known = false;
                foreach (var entry in _themeCores)
                {
                    if (entry.TryGetTarget(out var candidate) && ReferenceEquals(candidate, core))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known) _themeCores.Add(new WeakReference<CoreWebView2>(core));
            }

            await ApplyThemeStateAsync(
                new CoreThemeScriptTarget(core),
                ThemeState(core),
                () => DesiredEnabled(settings));
        }
        catch (Exception ex)
        {
            // A core being torn down throws here. The next control built from the same
            // settings registers a correct script anyway, so this is not fatal.
            Trace.WriteLine($"ContentScriptInjector: theme script registration failed: {ex.Message}");
        }
    }

    private ThemeScriptState ThemeState(CoreWebView2 core) => _themeStates.GetValue(core, _ => new ThemeScriptState());

    private List<CoreWebView2> LiveThemeCores()
    {
        var live = new List<CoreWebView2>();
        lock (_themeLock)
        {
            for (var i = _themeCores.Count - 1; i >= 0; i--)
            {
                if (_themeCores[i].TryGetTarget(out var core)) live.Add(core);
                else _themeCores.RemoveAt(i);
            }
        }
        live.Reverse();
        return live;
    }

    private bool DesiredEnabled(BrowserSettings? fallback)
    {
        if (_settings is not null) return _settings.AdaptiveToolbarTint;
        return fallback?.AdaptiveToolbarTint ?? false;
    }

    private Dispatcher? UiDispatcher => _dispatcher ??= Application.Current?.Dispatcher;

    /// <summary>
    /// Replaces the registered YouTube bootstrap on one core with one built from the
    /// settings as they are now, and removes it entirely when the feature is off.
    ///
    /// Every live core needs this, not only the ones already on YouTube: a core created
    /// while the feature was off has nothing registered, and a core created earlier
    /// carries settings that have since changed. Either way the next document it
    /// creates is the one that would run the stale text.
    /// </summary>
    public async Task UpdateYouTubeScriptAsync(CoreWebView2 core, BrowserSettings settings)
    {
        try
        {
            if (_youTubeScriptIds.TryGetValue(core, out var existing) && !string.IsNullOrEmpty(existing.Value))
            {
                core.RemoveScriptToExecuteOnDocumentCreated(existing.Value);
                existing.Value = null;
            }

            var script = _youtubeUnhook.GetScript(settings);
            if (string.IsNullOrEmpty(script)) return;

            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (_youTubeScriptIds.TryGetValue(core, out var slot)) slot.Value = id;
            else _youTubeScriptIds.Add(core, new StrongBox<string?>(id));
        }
        catch (Exception ex)
        {
            // A core being torn down throws here. The next control built from the same
            // settings registers a correct bootstrap anyway, so this is not fatal.
            Trace.WriteLine($"ContentScriptInjector: YouTube bootstrap update failed: {ex.Message}");
        }
    }

    /// <remarks>
    /// The ipcToken parameter is kept for call-site compatibility and deliberately unused.
    /// Injected scripts run in the page's world, so any value substituted into them is
    /// readable by page script. The token now stays in the binary and in the internal-page
    /// templates, which are loaded through NavigateToString and never exposed to a remote page.
    /// </remarks>
    public async Task InjectAsync(CoreWebView2 core, BrowserSettings settings, string ipcToken)
    {
        // YouTube Unhook is injected. The Shorts and sidebar features were
        // removed from it because they broke scrolling; see YouTubeUnhook.cs.
        // The enhancer and adnuke scripts stay parked in Parked/youtube.
        var unhook = _youtubeUnhook.GetScript(settings);
        if (!string.IsNullOrEmpty(unhook))
        {
            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(unhook);
            _youTubeScriptIds.Add(core, new StrongBox<string?>(id));
        }

        // Wallhaven Direct Downloader.
        //
        // The token parameter is deliberately unused for the injected scripts. It used to be
        // substituted into wallhaven-token.js as a global called window.__T, so any script
        // running on an allowlisted origin could read the session token and then drive every
        // privileged route. The scripts below carry no secret now: the bridge authorises the
        // wallhaven download by checking the message source origin, and the other two scripts
        // send values the sending page already knows.
        var hostsJson = string.Join(", ", _trustedExternalOrigins.Select(h => $"\"{h}\""));
        var hostGateScript = ResourceLoader.Load("Resources.Scripts.wallhaven-token.js")
            .Replace("__ALLOWED_HOSTS__", hostsJson);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(hostGateScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            ResourceLoader.Load("Resources.Scripts.wallhaven-downloader.js"));

        // Adaptive Theme Color Extractor (Ported from Adaptive-Tab-Bar-Colour Extension)
        //
        // The script is registered whatever the setting says, so a document that was
        // loaded while the setting was off can be switched on later without a reload.
        // The setting is baked into the registered text, and a live change reaches
        // the open document as a control message.
        await RegisterThemeScriptAsync(core, settings);

        // Link Preview - Alt plus click, on demand, no background timer
        if (settings.LinkPreviewEnabled)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                ResourceLoader.Load("Resources.Scripts.link-preview.js"));
        }
    }
}
