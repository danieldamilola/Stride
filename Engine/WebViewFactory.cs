using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
    private readonly string _ipcToken;
    private bool _extensionsLoaded;
    private int _extensionsInitStarted;

    public CoreWebView2Environment? Environment { get; private set; }

    /// <summary>Fires when the WebView2 browser process exits (engine decides what to do).</summary>
    public event Action? BrowserProcessExited;

    public WebViewFactory(BrowserSettings settings, ThemeManager themeManager, InternalPages pages, ExtensionManager extensionManager, string ipcToken)
    {
        _settings = settings;
        _themeManager = themeManager;
        _pages = pages;
        _extensionManager = extensionManager;
        _ipcToken = ipcToken;
    }

    /// <summary>Must be called once at startup to create the WebView2 environment.</summary>
    public async Task InitializeAsync()
    {
        var dataDir = Helpers.AppPaths.WebView2Dir;

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            // Fluent overlay scrollbar: overlays content without pushing layout, works in Shadow DOM
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            AdditionalBrowserArguments = BuildBrowserArguments(_settings.SmartScreenEnabled, _settings.ForceDarkMode) +
                (!_settings.HardwareAccelerationEnabled ? " --disable-gpu" : ""),
        };
        Environment = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

        if (_settings.AdBlockEnabled)
        {
            // Initializes the background adblock list (does not block startup, finishes in background if downloading)
            _ = AdBlockFilter.InitializeAsync();
        }

        // Handle browser process exit at the environment level
        Environment.BrowserProcessExited += (_, e) =>
        {
            Trace.WriteLine($"WebView2 browser process exited: Kind={e.BrowserProcessExitKind}");
            BrowserProcessExited?.Invoke();
        };
    }

    /// <summary>
    /// Builds the Chromium command-line flags. Notably:
    /// - The renderer process-limit flag is gone, restoring Chromium's default Site Isolation
    ///   (each site in its own process) rather than capping at a shared pool.
    /// - SmartScreen is ON by default. Disabling it via setting appends the disable flag.
    ///   Note: this flag is read at environment creation, so toggling requires a restart.
    /// - ForceDarkMode uses Chromium's native WebContentsForceDark engine.
    ///   Also read at environment creation - toggling requires a restart.
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
            "--allow-file-access-from-files " + // Allows local HTML files to execute modules and bypass strict file:// CORS
            "--no-first-run";

        if (forceDarkMode)
            args += " --enable-features=WebContentsForceDark";

        if (!smartScreenEnabled)
            args += " --disable-features=msSmartScreenProtection";

        return args;
    }

    public dynamic CreateWebViewControl(bool isInternal)
    {
        if (_settings.UseFloatingCommandBar)
        {
            return new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl
            {
                DefaultBackgroundColor = isInternal ? System.Drawing.Color.Transparent : (_settings.ForceDarkMode ? DarkBackground : System.Drawing.Color.White),
                Visibility = System.Windows.Visibility.Hidden
            };
        }

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
        core.SetVirtualHostNameToFolderMapping("temp.stride", System.IO.Path.GetTempPath(), CoreWebView2HostResourceAccessKind.Allow);

        // Strip native Edge bloat UI
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;

        try { core.Settings.IsSwipeNavigationEnabled = true; } catch (Exception ex) { Trace.WriteLine(ex); }
    }

    public void TryInitializeExtensions(dynamic wv)
    {
        if (_extensionsLoaded) return;
        // Only one in-flight init attempt. The flag resets on failure so a later
        // tab can retry if the first WebView was torn down before loading finished.
        if (Interlocked.Exchange(ref _extensionsInitStarted, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // MUST dispatch to UI thread!
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    CoreWebView2 core = wv.CoreWebView2;
                    await _extensionManager.InitializeAsync(core, _settings);
                    _extensionsLoaded = true;
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Extension init failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _extensionsInitStarted, 0);
            }
        });
    }

    public void NavigateInitialUrl(dynamic wv, BrowserTab tab)
    {
        // Settings/OneTab/History/Downloads/Onboarding are navigated by the NavigateTo*
        // calls after activation - skip here to avoid a double load.
        var callerManagedUrls = new System.Collections.Generic.HashSet<string>
        {
            InternalUrls.Settings, InternalUrls.OneTab, InternalUrls.History, InternalUrls.Downloads, InternalUrls.Onboarding, "internal://pending-native"
        };
        if (callerManagedUrls.Contains(tab.Url))
            return;

        // NewTab is served as HTML string so there's no real URL to navigate to.
        if (tab.Url == InternalUrls.NewTab || string.IsNullOrEmpty(tab.Url))
        {
            try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { Trace.WriteLine(ex); }
            return;
        }

        try { wv.CoreWebView2.Navigate(tab.Url); }
        catch (ArgumentException) { try { wv.CoreWebView2.NavigateToString(_pages.NewTabPage(_settings.NewTabShortcuts, _settings.AccentColor, InternalPages.HexToRgb(_settings.AccentColor), _ipcToken, _settings.DefaultZoom)); } catch (Exception ex) { Trace.WriteLine(ex); } }
    }
}