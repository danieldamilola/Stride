using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StrideBrowser.Helpers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

[Collection("ThemeManagerResources")]
public sealed class LightThemePaletteTests(ThemeAppFixture fixture)
{
    [Fact]
    public void LightTextAndControlBoundariesHaveReadableContrast()
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = AppThemeMode.Light });
            foreach (var foreground in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
                foreach (var surface in new[] { "BaseBrush", "SidebarBrush", "SurfaceBrush", "SurfaceHoverBrush", "SelectedTabBrush" })
                    Assert.True(Contrast(Color(foreground), Color(surface)) >= 4.5, foreground + " on " + surface);
            Assert.True(Contrast(Color("AddressFieldBorder"), Color("AddressFieldBackground")) >= 3);
            Assert.True(Contrast(Color("DangerBrush"), Color("SurfaceBrush")) >= 4.5);
        });
    }

    [Fact]
    public void SharedWebPaletteMatchesNativeLightSurfacesAndText()
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = AppThemeMode.Light });
            var css = ResourceLoader.Load("Resources.Pages.InternalPage.css");
            foreach (var selector in new[] { ":root[data-theme=\"light\"]", ":root[data-theme=\"system\"]" })
            {
                var block = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^{}]+)\}").Groups[1].Value;
                foreach (var pair in new Dictionary<string, string>
                {
                    ["--bg-base"]="BaseBrush", ["--bg-surface"]="SurfaceBrush", ["--bg-hover"]="SurfaceHoverBrush",
                    ["--text-main"]="TextPrimary", ["--text-muted"]="TextMuted", ["--control-border"]="ControlBorderBrush"
                })
                {
                    var value = Regex.Match(block, Regex.Escape(pair.Key) + @"\s*:\s*(#[0-9a-fA-F]{6})").Groups[1].Value;
                    Assert.Equal(Color(pair.Value), (System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
                }
            }
        });
    }

    [Fact]
    public void DarkSiteOnLightAppUsesReadableScopedChromeAndClearsBackToLight()
    {
        fixture.RunOnAppThread(() =>
        {
            _ = new ThemeManager(new BrowserSettings { AppTheme = AppThemeMode.Light });
            var toolbar = new Border { Background = new SolidColorBrush(Color("SidebarBrush")) };
            var address = new Border(); var popup = new Border();
            var adapter = new ToolbarTintAdapter(toolbar, address, popup);
            var tab = new BrowserTab { ThemeColor = "#111111" };
            adapter.UpdateTint(tab, true);
            var text = ((SolidColorBrush)toolbar.FindResource("TextPrimary")).Color;
            var surface = ((SolidColorBrush)toolbar.FindResource("SurfaceBrush")).Color;
            Assert.True(Contrast(text, surface) >= 4.5);
            Assert.True(Contrast(text, (System.Windows.Media.Color)ColorConverter.ConvertFromString("#111111")) >= 4.5);
            tab.ThemeColor = ""; adapter.UpdateTint(tab, true);
            Assert.False(toolbar.Resources.Contains("TextPrimary"));
            Assert.Equal(Color("TextPrimary"), ((SolidColorBrush)toolbar.FindResource("TextPrimary")).Color);
            Assert.Equal(System.Windows.Media.Colors.White, ((SolidColorBrush)toolbar.FindResource("AddressFieldBackground")).Color);
        });
    }

    private static System.Windows.Media.Color Color(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
    private static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        static double Luminance(System.Windows.Media.Color c)
        {
            static double Linear(byte v) { var s=v/255d; return s<=0.04045 ? s/12.92 : Math.Pow((s+0.055)/1.055,2.4); }
            return 0.2126*Linear(c.R)+0.7152*Linear(c.G)+0.0722*Linear(c.B);
        }
        return (Math.Max(Luminance(a),Luminance(b))+0.05)/(Math.Min(Luminance(a),Luminance(b))+0.05);
    }
}
