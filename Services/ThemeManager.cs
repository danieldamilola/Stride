using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

public static class ThemeManager
{
    private static BrowserSettings? _settings;

    public static event Action? ThemeChanged;

    public static void Initialize(BrowserSettings settings)
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

    public static void ApplyTheme()
    {
        if (_settings is null) return;

        bool isDark = _settings.AppTheme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemThemeDark()
        };

        var uri = new Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        
        var dict = new ResourceDictionary { Source = uri };

        // Replace the first merged dictionary (which we set to DarkTheme in App.xaml)
        if (Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            Application.Current.Resources.MergedDictionaries[0] = dict;
        }
        else
        {
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        ThemeChanged?.Invoke();
    }

    public static bool IsCurrentlyDark()
    {
        if (_settings is null) return true;
        return _settings.AppTheme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemThemeDark()
        };
    }

    public static string GetThemeString()
    {
        return _settings?.AppTheme.ToString().ToLowerInvariant() ?? "system";
    }

    private static bool IsSystemThemeDark()
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
