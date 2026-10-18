using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine;

/// <summary>
/// Owns WebView2 environment + control creation and configuration:
/// browser arguments, per-control defaults, virtual host mappings, extensions.
/// TabEngine drives the lifecycle; this class holds the WebView2-specific knowledge.
/// </summary>
public sealed class WebViewFactory
{
    public static readonly System.Drawing.Color DarkBackground = System.Drawing.Color.FromArgb(255, 24, 24, 30);

    private readonly BrowserSettings _settings;
    private readonly ThemeManager _themeManager;
    private readonly InternalPages _pages;
    private readonly ExtensionManager _extensionManager;
    private readonly ExtensionStartupCoordinator _extensionStartup;
    private readonly Dispatcher _uiDispatcher;
    private readonly string _ipcToken;
    private readonly string _dataDirectory;
    private readonly string _profileKey;
    private readonly string _ownerId = Guid.NewGuid().ToString("N");

    public CoreWebView2Environment? Environment { get; private set; }

    /// <summary>Fires when the WebView2 browser process exits (engine decides what to do).</summary>
    public event Action? BrowserProcessExited;

    public WebViewFactory(
        BrowserSettings settings,
        ThemeManager themeManager,
        InternalPages pages,
        ExtensionManager extensionManager,
        ExtensionStartupCoordinator extensionStartup,
        string ipcToken,
        string? dataDirectory = null)
    {
        _settings = settings;
        _themeManager = themeManager;
        _pages = pages;
        _extensionManager = extensionManager;
        _extensionStartup = extensionStartup;
        _ipcToken = ipcToken;
        _dataDirectory = dataDirectory ?? Helpers.AppPaths.WebView2Dir;
        // Every window in the process shares one WebView2 user data folder, and that
        // folder is the profile the extension registration lives in. Keying the
        // coordinator on it is what makes two windows share one registration instead
        // of each keeping its own per window flag over the same profile.
        _profileKey = ExtensionStartupCoordinator.ProfileKeyFor(_dataDirectory);
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>The profile key this window's extensions are registered under.</summary>
    public string ExtensionProfileKey => _profileKey;

    /// <summary>Must be called once at startup to create the WebView2 environment.</summary>
    public async Task InitializeAsync()
    {
        var dataDir = _dataDirectory;

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            // Fluent overlay scrollbar: overlays content without pushing layout, works in Shadow DOM
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            AdditionalBrowserArguments = BuildBrowserArguments(_settings.SmartScreenEnabled, _settings.ForceDarkMode) +
                (!_settings.HardwareAccelerationEnabled ? " --disable-gpu" : ""),
        };
        Environment = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

        // AdBlockEnabled is consumed by ExtensionManager, which enables or disables the
        // uBlock Origin extension. The browser side keeps no blocklist of its own: the
        // AdBlockFilter that used to be initialized here downloaded a domain list at
        // startup and, from Apply, answered matching requests with an empty 200.

        // Handle browser process exit at the environment level
        Environment.BrowserProcessExited += (_, e) =>
        {
            Trace.WriteLine($"WebView2 browser process exited: Kind={e.BrowserProcessExitKind}");
            BrowserProcessExited?.Invoke();
        };
    }

        /// <summary>
    /// Builds the Chromium command-line flags. Notably:
    /// - Process-per-site groups pages from the same site into one renderer process, which
    ///   reduces process isolation between distinct origins on the same registrar domain.
    ///   This is a memory/performance tradeoff; a separate process-per-site-instance flag
    ///   would be needed for strict site isolation.
    /// - SmartScreen is ON by default. Disabling it via setting appends the disable flag.
    ///   Note: this flag is read at environment creation, so toggling requires a restart.
    /// - ForceDarkMode uses Chromium's native WebContentsForceDark engine.
    ///   Also read at environment creation - toggling requires a restart.
    /// - Autoplay policy is deliberately left at the WebView2 default.
    ///   An earlier revision added --autoplay-policy=no-user-gesture-required here.
    ///   It was retracted. The evidence behind it was a loopback probe page, not
    ///   the reported YouTube symptom, and Chrome's own autoplay policy
    ///   (developer.chrome.com/blog/autoplay/) deliberately blocks unmuted
    ///   autoplay until a site is engaged; the flag disables that policy entirely
    ///   and Chrome documents it as a developer testing switch. Microsoft lists
    ///   it as a settable flag, which is not a product recommendation. Removing
    ///   it restores the default policy for the product. Do not re-add it as a
    ///   blanket policy change without a reproduction of the actual symptom on a
    ///   real watch page.
    /// </summary>
    public static string BuildBrowserArguments(bool smartScreenEnabled, bool forceDarkMode = false)
    {
        var args =
            "--app-user-model-id=Stride " +
            "--disable-background-networking " +
            "--disable-breakpad " +
            "--disable-component-update " +
            "--disable-default-apps " +
            "--disable-domain-reliability " +
            "--disable-sync " +
            "--metrics-recording-only " +
            "--process-per-site " + // Groups pages from the same site into the same process
            "--no-first-run";

        if (forceDarkMode)
            args += " --enable-features=WebContentsForceDark";

        if (!smartScreenEnabled)
            args += " --disable-features=msSmartScreenProtection";

        return args;
    }

    /// <summary>
    /// Creates the WPF control for a tab.
    ///
    /// The renderer type is fixed to the HwndHost based WebView2. That is the
    /// control this baseline actually used in production: the floating command
    /// bar setting that used to switch to WebView2CompositionControl defaulted
    /// to false, so every normal run created this control. The setting is gone,
    /// and with it the only caller that could ask for the composition control.
    ///
    /// Starts Collapsed, not Hidden. HwndHost ignores Hidden and keeps the child
    /// HWND alive and rendering behind the visible tab, so a background tab would
    /// keep burning CPU and a newly created tab would present a stale frame.
    /// TabEngine.ShowOnlyActiveWebView decides the visible state.
    /// </summary>
    public dynamic CreateWebViewControl(bool isInternal)
    {
        return new Microsoft.Web.WebView2.Wpf.WebView2
        {
            DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White),
            Visibility = System.Windows.Visibility.Collapsed
        };
    }

    public void ConfigureCoreWebView(CoreWebView2 core)
    {
        core.Profile.PreferredColorScheme = _themeManager.IsCurrentlyDark()
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;

        core.SetVirtualHostNameToFolderMapping("local.assets", System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages"), CoreWebView2HostResourceAccessKind.Allow);
        var userAssetsPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (!System.IO.Directory.Exists(userAssetsPath)) System.IO.Directory.CreateDirectory(userAssetsPath);
        core.SetVirtualHostNameToFolderMapping("user.assets", userAssetsPath, CoreWebView2HostResourceAccessKind.Allow);
        // NOTE: no temp.stride mapping. The temp directory is user-owned and must never be
        // reachable from web content (it would expose %TEMP% to any page). Removed deliberately.

        // Strip native Edge bloat UI
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        // Align reputation checking with the user's SmartScreen preference
        try { core.Settings.IsReputationCheckingRequired = _settings.SmartScreenEnabled; } catch (Exception ex) { Trace.WriteLine(ex); }

        try { core.Settings.IsSwipeNavigationEnabled = true; } catch (Exception ex) { Trace.WriteLine(ex); }
    }

    /// <summary>
    /// Waits until the profile this control belongs to holds the requested native
    /// extension state, or reports an honest failure.
    ///
    /// The work itself is shared per profile by <see cref="ExtensionStartupCoordinator"/>,
    /// so a second window on the same profile waits on the registration the first one
    /// started rather than registering again. Two isolated profiles stay independent
    /// because their keys differ.
    /// </summary>
    public async Task<ExtensionStartupResult> EnsureExtensionsReadyAsync(
        dynamic wv,
        bool? desiredEnabled = null,
        CancellationToken cancellationToken = default)
    {
        var desired = desiredEnabled ?? _settings.AdBlockEnabled;
        var core = SafeCore(wv);
        if (core is null)
            return ExtensionStartupResult.NoCore(
                _profileKey, desired, "the tab has no live core to run the profile calls on");

        var result = await ApplyPreferenceAsync(core, desired, cancellationToken);
        Trace.WriteLine(
            $"WebViewFactory: extension gate profile={_profileKey} native={NativeProfileOf(core)} owner={_ownerId} " +
            $"requested={desired} -> {result}");
        return result;
    }

    /// <summary>
    /// The same gate for a core the caller already holds, which is what a settings
    /// toggle uses: the user turned the preference on or off and the profile has to
    /// follow now, with no reload and no remove and add again.
    /// </summary>
    public Task<ExtensionStartupResult> ApplyPreferenceAsync(
        CoreWebView2 core,
        bool desiredEnabled,
        CancellationToken cancellationToken = default)
    {
        var request = new ExtensionStartupRequest(
            _profileKey, desiredEnabled, _ownerId, BuildReconciler(core), NativeProfileOf(core));
        return _extensionStartup.EnsureReadyAsync(request, null, cancellationToken);
    }

    /// <summary>
    /// The profile the control actually talks to, when the platform will say. The user
    /// data folder is the key the run is serialized on, because that is what the
    /// environments in one process share. This is what keeps a cached answer from
    /// being handed to a different profile inside the same folder.
    /// </summary>
    private static string? NativeProfileOf(CoreWebView2 core)
    {
        try { return core.Profile.ProfilePath; }
        catch (Exception ex)
        {
            Trace.WriteLine($"WebViewFactory: native profile path unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Binds one core's WebView2 calls into a delegate the coordinator can await.
    ///
    /// The calls have to happen on the thread that owns the control, which is not
    /// necessarily the thread the coordinator was called from, so the delegate
    /// marshals. Dispatcher.InvokeAsync on an async lambda returns
    /// DispatcherOperation&lt;Task&lt;T&gt;&gt;, so awaiting its Task directly would
    /// wait only for the call to be queued. Unwrap is what makes the wait cover the
    /// registration itself.
    /// </summary>
    private ExtensionReconcileCore BuildReconciler(CoreWebView2 core)
    {
        return async (desiredEnabled, cancellationToken) =>
        {
            var operation = _uiDispatcher.InvokeAsync(
                async () => await _extensionManager.ReconcileAsync(core, desiredEnabled),
                DispatcherPriority.Normal,
                cancellationToken);
            return await operation.Task.Unwrap();
        };
    }

    private static CoreWebView2? SafeCore(dynamic wv)
    {
        try { return wv?.CoreWebView2 as CoreWebView2; }
        catch (Exception ex)
        {
            Trace.WriteLine($"WebViewFactory: core not available: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stage timings from the last disk preparation, so a caller can report where
    /// startup time went without a profiler.
    /// </summary>
    public ExtensionManager.PreparationTimings LastExtensionPreparation => _extensionManager.LastPreparation;

    public void NavigateInitialUrl(dynamic wv, BrowserTab tab)
    {
        // Settings/OneTab/History/Downloads/Onboarding are navigated by the NavigateTo*
        // calls after activation - skip here to avoid a double load.
        var callerManagedUrls = new System.Collections.Generic.HashSet<string>
        {
            InternalUrls.Settings, InternalUrls.OneTab, InternalUrls.History, InternalUrls.Downloads, InternalUrls.Onboarding, InternalUrls.ReleaseNotes, "internal://pending-native"
        };
        if (callerManagedUrls.Contains(tab.Url))
            return;

        // NewTab is served as HTML string so there's no real URL to navigate to.
        if (tab.Url == InternalUrls.NewTab || string.IsNullOrEmpty(tab.Url))
        {
            tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.NewTab, _ipcToken, 1);
            try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { Trace.WriteLine(ex); }
            return;
        }

        try { wv.CoreWebView2.Navigate(tab.Url); }
        catch (ArgumentException)
        {
            try
            {
                tab.ActiveInternalDocument = new InternalDocumentIdentity(tab.Id, TabEngine.InternalPageKind.NewTab, _ipcToken, 1);
                wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom));
            }
            catch (Exception ex) { Trace.WriteLine(ex); }
        }
    }
}
