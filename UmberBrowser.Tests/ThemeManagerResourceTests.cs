using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using UmberBrowser.Models;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Tests for ThemeManager resource loading fix.
/// Verifies that theme dictionaries load correctly via pack URIs from the Umber assembly.
/// Tests actual effective resource values, not just settings booleans.
/// </summary>
[Collection("ThemeManagerResources")]
public sealed class ThemeManagerResourceTests
{
    private readonly ThemeAppFixture _fixture;

    public ThemeManagerResourceTests(ThemeAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DarkThemeDictionary_LoadsViaPackUri_FromUmberAssembly()
    {
        _fixture.RunOnAppThread(() =>
        {
            var uri = new Uri("pack://application:,,,/Umber;component/Themes/DarkTheme.xaml", UriKind.Absolute);
            var dict = new ResourceDictionary { Source = uri };
            
            Assert.NotNull(dict);
            Assert.True(dict.Count > 0);
            
            // Verify specific known keys from the compiled DarkTheme.xaml
            Assert.Contains("SurfaceBrush", dict.Keys.Cast<string>());
            Assert.Contains("TextPrimary", dict.Keys.Cast<string>());
            Assert.Contains("PrimaryButtonBrush", dict.Keys.Cast<string>());
            Assert.DoesNotContain("AccentColor", dict.Keys.Cast<string>());
        });
    }

    [Fact]
    public void LightThemeDictionary_LoadsViaPackUri_FromUmberAssembly()
    {
        _fixture.RunOnAppThread(() =>
        {
            var uri = new Uri("pack://application:,,,/Umber;component/Themes/LightTheme.xaml", UriKind.Absolute);
            var dict = new ResourceDictionary { Source = uri };
            
            Assert.NotNull(dict);
            Assert.True(dict.Count > 0);
            Assert.Contains("SurfaceBrush", dict.Keys.Cast<string>());
            Assert.Contains("TextPrimary", dict.Keys.Cast<string>());
        });
    }

    [Fact]
    public void ThemeManager_ApplyDarkAndLight_ThemesLoadAndApplyEffectiveResources()
    {
        _fixture.RunOnAppThread(() =>
        {
            var settings = new BrowserSettings { AppTheme = AppThemeMode.Dark };
            var themeManager = new ThemeManager(settings);
            
            themeManager.ApplyTheme();
            
            // Verify effective dark theme resources are applied
            var surfaceBrush = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
            Assert.NotNull(surfaceBrush);
            Assert.Equal(Color.FromRgb(0x22, 0x22, 0x22), surfaceBrush.Color); // Dark theme SurfaceColor = #222222
            
            var textPrimary = Application.Current.FindResource("TextPrimary") as SolidColorBrush;
            Assert.NotNull(textPrimary);
            Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), textPrimary.Color); // Dark theme TextPrimaryColor = #EEEEEE
            
            // Switch to light
            settings.AppTheme = AppThemeMode.Light;
            themeManager.ApplyTheme();
            
            surfaceBrush = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
            Assert.NotNull(surfaceBrush);
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), surfaceBrush.Color); // Light theme SurfaceColor = #FFFFFF
            
            textPrimary = Application.Current.FindResource("TextPrimary") as SolidColorBrush;
            Assert.NotNull(textPrimary);
            Assert.Equal(Color.FromRgb(0x20, 0x20, 0x20), textPrimary.Color); // Light theme TextPrimaryColor = #202020
        });
    }

    [Fact]
    public void ThemeManager_AppThemePropertyChanged_AppliesThemeAndSaves()
    {
        _fixture.RunOnAppThread(() =>
        {
            var testFile = Path.Combine(Path.GetTempPath(), $"Umber_ThemeTest_{Guid.NewGuid():N}.json");
            try
            {
                var settingsStore = new SettingsStore(testFile);
                var settings = new BrowserSettings { AppTheme = AppThemeMode.System };
                var themeManager = new ThemeManager(settings);
                
                settings.AppTheme = AppThemeMode.Dark;
                Assert.True(themeManager.IsCurrentlyDark());
                
                // Verify effective resource change
                var surfaceBrush = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
                Assert.NotNull(surfaceBrush);
                Assert.Equal(Color.FromRgb(0x22, 0x22, 0x22), surfaceBrush.Color);
                
                settingsStore.Save(settings);
                var loadedSettings = settingsStore.Load();
                Assert.Equal(AppThemeMode.Dark, loadedSettings.AppTheme);
            }
            finally
            {
                if (File.Exists(testFile)) File.Delete(testFile);
            }
        });
    }

    [Fact]
    public void ThemeManager_ApplyTheme_PreservesOtherMergedDictionaries()
    {
        _fixture.RunOnAppThread(() =>
        {
            // Add a sentinel dictionary with a unique non-theme key
            var sentinelDict = new ResourceDictionary();
            sentinelDict["ThemeManagerTestSentinel"] = "SentinelValue";
            Application.Current.Resources.MergedDictionaries.Add(sentinelDict);
            
            var initialCount = Application.Current.Resources.MergedDictionaries.Count;
            Assert.True(initialCount >= 2); // DarkTheme + LightTheme + Sentinel
            
            var settings = new BrowserSettings { AppTheme = AppThemeMode.Dark };
            var themeManager = new ThemeManager(settings);
            themeManager.ApplyTheme();
            
            // ThemeManager replaces the first dictionary, count should remain the same
            Assert.Single(Application.Current.Resources.MergedDictionaries.Where(d => d.Source?.OriginalString.Contains("Theme.xaml") == true));
            
            // Sentinel dictionary should survive
            Assert.True(Application.Current.Resources.MergedDictionaries
                .OfType<ResourceDictionary>()
                .Any(d => d.Contains("ThemeManagerTestSentinel")));
            Assert.Equal("SentinelValue", Application.Current.Resources["ThemeManagerTestSentinel"]);
        });
    }

    [Fact]
    public void ThemeManager_SwitchThemeTwice_ReloadsCorrectDictionaryAndPreservesSentinel()
    {
        _fixture.RunOnAppThread(() =>
        {
            // Add sentinel
            var sentinelDict = new ResourceDictionary();
            sentinelDict["ThemeManagerTestSentinel"] = "SentinelValue";
            Application.Current.Resources.MergedDictionaries.Add(sentinelDict);
            
            var settings = new BrowserSettings { AppTheme = AppThemeMode.Light };
            var themeManager = new ThemeManager(settings);
            
            themeManager.ApplyTheme();
            Assert.False(themeManager.IsCurrentlyDark());
            Assert.Equal("SentinelValue", Application.Current.Resources["ThemeManagerTestSentinel"]);
            
            var surfaceBrush = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), surfaceBrush.Color); // Light
            
            settings.AppTheme = AppThemeMode.Dark;
            themeManager.ApplyTheme();
            Assert.True(themeManager.IsCurrentlyDark());
            Assert.Equal("SentinelValue", Application.Current.Resources["ThemeManagerTestSentinel"]);
            
            var surfaceBrushDark = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
            Assert.Equal(Color.FromRgb(0x22, 0x22, 0x22), surfaceBrushDark.Color); // Dark
            
            settings.AppTheme = AppThemeMode.Light;
            themeManager.ApplyTheme();
            Assert.False(themeManager.IsCurrentlyDark());
            Assert.Equal("SentinelValue", Application.Current.Resources["ThemeManagerTestSentinel"]);
            
            var surfaceBrushLight = Application.Current.FindResource("SurfaceBrush") as SolidColorBrush;
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), surfaceBrushLight.Color); // Light
        });
    }

    [Fact]
    public void ThemeManager_RelativeUriFails_AssemblyQualifiedUriSucceeds()
    {
        _fixture.RunOnAppThread(() =>
        {
            // BEFORE: Relative URI should fail in non-Umber host
            var relativeUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
            var relativeDict = new ResourceDictionary();
            var relativeFailed = false;
            try
            {
                relativeDict.Source = relativeUri;
            }
            catch (Exception)
            {
                relativeFailed = true;
            }
            Assert.True(relativeFailed, "Relative URI should fail in non-Umber host");
            
            // AFTER: Assembly-qualified URI should succeed
            var assemblyName = typeof(UmberBrowser.App).Assembly.GetName().Name;
            var absoluteUri = new Uri($"pack://application:,,,/{assemblyName};component/Themes/DarkTheme.xaml", UriKind.Absolute);
            var absoluteDict = new ResourceDictionary { Source = absoluteUri };
            
            Assert.NotNull(absoluteDict);
            Assert.True(absoluteDict.Count > 0);
            Assert.Contains("SurfaceBrush", absoluteDict.Keys.Cast<string>());
        });
    }

    [Fact]
    public void NonThemeSetting_Roundtrip_WorksIndependently()
    {
        _fixture.RunOnAppThread(() =>
        {
            var testFile = Path.Combine(Path.GetTempPath(), $"Umber_NonThemeTest_{Guid.NewGuid():N}.json");
            try
            {
                var settingsStore = new SettingsStore(testFile);
                var settings = new BrowserSettings();
                var themeManager = new ThemeManager(settings); // Ensure ThemeManager is active
                
                // Toggle a non-theme setting
                settings.SearchSuggestionsEnabled = !settings.SearchSuggestionsEnabled;
                var expectedValue = settings.SearchSuggestionsEnabled;
                
                settingsStore.Save(settings);
                var loadedSettings = settingsStore.Load();
                Assert.Equal(expectedValue, loadedSettings.SearchSuggestionsEnabled);
            }
            finally
            {
                if (File.Exists(testFile)) File.Delete(testFile);
            }
        });
    }
}

/// <summary>
/// Provides a plain System.Windows.Application instance on a dedicated STA thread.
/// Does NOT use UmberBrowser.App - no OnStartup, no Composition, no user profile access.
/// Only provides a valid Application.Current with dispatcher for resource loading.
/// </summary>
public sealed class ThemeAppFixture : IDisposable
{
    private readonly Thread _appThread;
    private readonly TaskCompletionSource<object?> _startupTcs;
    private Application _app;
    private Dispatcher _dispatcher;
    private bool _disposed;

    public ThemeAppFixture()
    {
        _startupTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appThread = new Thread(RunApp);
        _appThread.SetApartmentState(ApartmentState.STA);
        _appThread.IsBackground = true;
        _appThread.Start();
        
        // Wait for Application.Current to be created and initialized
        _startupTcs.Task.GetAwaiter().GetResult();
    }

    private void RunApp()
    {
        try
        {
            // Create plain Application - no UmberBrowser.App, no OnStartup logic
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _app = app;
            
            // Pre-load BOTH theme dictionaries using relative pack URIs
            // Application.LoadComponent requires relative URIs in format: AssemblyName;component/Path.xaml
            var assemblyName = typeof(UmberBrowser.App).Assembly.GetName().Name;
            var darkUri = new Uri($"{assemblyName};component/Themes/DarkTheme.xaml", UriKind.Relative);
            var lightUri = new Uri($"{assemblyName};component/Themes/LightTheme.xaml", UriKind.Relative);
            
            // Load theme dictionaries using Application.LoadComponent (standard WPF pattern)
            var darkDict = (ResourceDictionary)Application.LoadComponent(darkUri);
            var lightDict = (ResourceDictionary)Application.LoadComponent(lightUri);
            
            // Add both to merged dictionaries (ThemeManager will replace the first one)
            Application.Current.Resources.MergedDictionaries.Add(darkDict);

            
            _dispatcher = Dispatcher.CurrentDispatcher;
            _startupTcs.TrySetResult(null);
            
            // Run the dispatcher loop
            app.Run();
        }
        catch (Exception ex)
        {
            _startupTcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Runs an action on the App thread (STA thread with Application.Current set).
    /// </summary>
    public void RunOnAppThread(Action action)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ThemeAppFixture));

        if (Application.Current == null)
            throw new InvalidOperationException("Application not initialized");

        _dispatcher.Invoke(() =>
        {
            // Ensure theme resources are available (idempotent)
            EnsureThemeResourcesLoaded();
            action();
        });
    }

    private void EnsureThemeResourcesLoaded()
    {
        // Idempotent: only add if not already present
        var hasDark = Application.Current.Resources.MergedDictionaries
            .OfType<ResourceDictionary>()
            .Any(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));
        
        if (!hasDark)
        {
            var darkDict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{typeof(UmberBrowser.App).Assembly.GetName().Name};component/Themes/DarkTheme.xaml", UriKind.Absolute) };
            var lightDict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{typeof(UmberBrowser.App).Assembly.GetName().Name};component/Themes/LightTheme.xaml", UriKind.Absolute) };
            Application.Current.Resources.MergedDictionaries.Add(darkDict);

        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            if (_dispatcher != null && _app != null)
            {
                _dispatcher.Invoke(() =>
                {
                    _app.Shutdown();
                });
            }
        }
        catch { }
        
        _appThread.Join(TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// Collection fixture for ThemeManager tests - ensures single plain Application instance per test run.
/// </summary>
[CollectionDefinition("ThemeManagerResources", DisableParallelization = true)]
public sealed class ThemeManagerResourcesCollection : ICollectionFixture<ThemeAppFixture>
{
}