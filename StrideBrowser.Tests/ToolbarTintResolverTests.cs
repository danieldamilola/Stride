using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ToolbarTintResolverTests
{
    private static readonly Color Base = Color.FromRgb(0x1A, 0x1A, 0x2E);

    [Fact]
    public void ResolveTargetColor_EmptyHex_ReturnsBase()
    {
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor("", Base));
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor(null, Base));
    }

    [Fact]
    public void ResolveTargetColor_ValidHex_ReturnsColor()
    {
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56),
            ToolbarTintResolver.ResolveTargetColor("#123456", Base));
    }

    [Fact]
    public void ResolveTargetColor_WhiteHex_FallsBackToDark()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x11, 0x11),
            ToolbarTintResolver.ResolveTargetColor("#FFFFFF", Base));
    }

    [Fact]
    public void ResolveTargetColor_StrongGreen_FallsBackToDark()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x11, 0x11),
            ToolbarTintResolver.ResolveTargetColor("#00C853", Base));
    }

    [Fact]
    public void ResolveTargetColor_BadHex_ReturnsBase()
    {
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor("not-a-color", Base));
    }

    [Fact]
    public void IsLight_ClassifiesCorrectly()
    {
        Assert.True(ToolbarTintResolver.IsLight(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        Assert.False(ToolbarTintResolver.IsLight(Color.FromRgb(0x11, 0x11, 0x11)));
    }

    /// <summary>
    /// The address field repaints itself from the popup surface while suggestions are
    /// open, so a popup surface that ignores the theme also makes the field
    /// unreadable. Both themes must therefore carry the whole suggestion palette and
    /// the text on it must have real contrast.
    /// </summary>
    [Theory]
    [InlineData("LightTheme.xaml", true)]
    [InlineData("DarkTheme.xaml", false)]
    public void ThemeSuggestionPalette_MatchesTheThemeAndCarriesReadableText(string file, bool expectLight)
    {
        var theme = ReadTheme(file);

        foreach (var key in new[] { "SuggestionPopupBackground", "SuggestionPopupBorder", "SuggestionHoverBackground" })
            Assert.True(theme.ContainsKey(key), $"{file} does not define {key}");

        var surface = Parse(theme["SuggestionPopupBackground"]);
        var text = Parse(theme["TextPrimary"]);
        var hover = Parse(theme["SuggestionHoverBackground"]);

        Assert.Equal(expectLight, ToolbarTintResolver.IsLight(Media(surface)));
        Assert.True(Contrast(surface, text) >= 4.5,
            $"{file} popup surface {Hex(surface)} against text {Hex(text)} is below a readable contrast");
        Assert.NotEqual(surface, hover);
    }

    /// <summary>
    /// A theme that ships the popup in one palette and the suggestion text in another
    /// is the exact failure that shipped. The row text has to come from the theme.
    /// </summary>
    [Fact]
    public void SuggestionRowText_IsNotAHardCodedLiteral()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "MainWindow.xaml"));
        var row = xaml.Substring(xaml.IndexOf("SuggestionItemTemplate", StringComparison.Ordinal));
        row = row.Substring(0, row.IndexOf("TabItemTemplate", StringComparison.Ordinal));

        Assert.Contains("Text=\"{Binding DisplayText}\"", row);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimary}\"", row);
        Assert.DoesNotContain("Foreground=\"#", row);
    }

    // ── theme file reading ──

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stride.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    /// <summary>Brush key to resolved colour, following the StaticResource aliases the themes use.</summary>
    private static Dictionary<string, string> ReadTheme(string file)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "Themes", file));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        // Build the key name from the namespace. XName.Get("x:Key") only resolves a prefix
        // against ambient declarations it was given, not the ones in this document, and falls
        // back to treating the whole string as a local name, which a colon makes illegal.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var key = x + "Key";

        // A <Color> key carries its hex as element content, a brush carries its source as an
        // attribute. The themes are written both ways.
        var colors = doc.Descendants(ns + "Color")
            .Where(e => e.Attribute(key) is not null)
            .ToDictionary(e => e.Attribute(key)!.Value, e => e.Value.Trim());

        var brushes = new Dictionary<string, string>();
        foreach (var brush in doc.Descendants(ns + "SolidColorBrush"))
        {
            if (brush.Attribute(key) is null) continue;
            var color = brush.Attribute("Color")!.Value;
            var alias = color.StartsWith("{StaticResource ", StringComparison.Ordinal)
                ? color["{StaticResource ".Length..^1]
                : null;
            brushes[brush.Attribute(key).Value] = alias is null ? color : colors[alias];
        }
        return brushes;
    }

    private static (byte R, byte G, byte B) Parse(string hex) =>
        (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private static Color Media((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    private static string Hex((byte R, byte G, byte B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var high = Math.Max(Luminance(a), Luminance(b));
        var low = Math.Min(Luminance(a), Luminance(b));
        return (high + 0.05) / (low + 0.05);
    }

    private static double Luminance((byte R, byte G, byte B) c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte value)
    {
        var v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
