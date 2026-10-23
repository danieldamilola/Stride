using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using UmberBrowser.Models;

namespace UmberBrowser.Services;

public sealed class ThemeManager
{
    private readonly BrowserSettings _settings;

    public event Action? ThemeChanged;

    public ThemeManager(BrowserSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BrowserSettings.AppTheme))
            {
                ApplyTheme();
            }
        };

        // Listen for OS theme changes
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _settings.AppTheme == AppThemeMode.System)
            {
                Application.Current.Dispatcher.Invoke(ApplyTheme);
            }
        };

        ApplyTheme();
    }

    public void ApplyTheme()
    {

        bool isDark = _settings.AppTheme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemThemeDark()
        };

        var assemblyName = typeof(ThemeManager).Assembly.GetName().Name;
        var uri = new Uri($"pack://application:,,,/{assemblyName};component/Themes/{(isDark ? "DarkTheme.xaml" : "LightTheme.xaml")}", UriKind.Absolute);
        
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        var themes = merged.Where(d => d.Source?.OriginalString.EndsWith("Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) == true ||
            d.Source?.OriginalString.EndsWith("Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase) == true).ToList();
        var index = themes.Count == 0 ? 0 : merged.IndexOf(themes[0]);
        foreach (var previous in themes) merged.Remove(previous);
        merged.Insert(Math.Min(index, merged.Count), dict);

        ThemeChanged?.Invoke();
    }

    public bool IsCurrentlyDark()
    {
        return _settings.AppTheme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemThemeDark()
        };
    }

    public string GetThemeString()
    {
        return _settings.AppTheme.ToString().ToLowerInvariant();
    }

    private bool IsSystemThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
            {
                return lightTheme == 0;
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Theme error: {ex}"); }
        return true; // Default to dark if we can't read the registry
    }
}
