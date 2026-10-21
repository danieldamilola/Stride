using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using StrideBrowser.Services.Extensions;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionManifestReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stride-extension-metadata-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jG7sAAAAASUVORK5CYII=");

    public ExtensionManifestReaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Read_ReturnsMetadataAndLargestSensibleLocalIcon()
    {
        Write("small.png", Png);
        Write("large.png", Png.Concat(new byte[] { 0 }).ToArray());
        Write("popup.html", "<p>Popup</p>");
        Write("options/index.html", "<p>Options</p>");
        Manifest(new
        {
            description = "A useful extension", version = "1.2.3",
            icons = new Dictionary<string, string> { ["16"] = "small.png", ["128"] = "small.png", ["256"] = "large.png" },
            action = new { default_popup = "popup.html" }, options_ui = new { page = "options/index.html" },
            permissions = new[] { "storage", "tabs" }, host_permissions = new[] { "https://example.com/*", "tabs" }
        });

        var info = ExtensionManifestReader.Read(_root);

        Assert.Equal("A useful extension", info.Description);
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal("popup.html", info.PopupPath);
        Assert.Equal("options/index.html", info.OptionsPath);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png.Concat(new byte[] { 0 }).ToArray()), info.IconDataUri);
        Assert.Equal(new[] { "storage", "tabs", "https://example.com/*" }, info.Permissions);
    }

    [Fact]
    public void Read_LocalizesDefaultLocaleAndCaseInsensitiveMessageKey()
    {
        Manifest(new { description = "__MSG_extensionDescription__", default_locale = "fr" });
        Write("_locales/fr/messages.json", """{"extensiondescription":{"message":"Description française"}}""");
        Write("_locales/en/messages.json", """{"extensionDescription":{"message":"English description"}}""");
        Assert.Equal("Description française", ExtensionManifestReader.Read(_root).Description);
    }

    [Theory]
    [InlineData("fr", "invalid JSON")]
    [InlineData("../outside", "{}")]
    [InlineData("missing", "{}")]
    public void Read_FallsBackToEnglishForMissingOrInvalidLocale(string locale, string content)
    {
        Manifest(new { description = "__MSG_description__", default_locale = locale });
        Write("_locales/fr/messages.json", content);
        Write("_locales/en/messages.json", """{"description":{"message":"English description"}}""");
        Assert.Equal("English description", ExtensionManifestReader.Read(_root).Description);
    }

    [Fact]
    public void Read_UnresolvedLocalizationDoesNotDisplayRawPlaceholder()
    {
        Manifest(new { description = "__MSG_missing__" });
        Assert.Equal("", ExtensionManifestReader.Read(_root).Description);
    }

    [Fact]
    public void Read_OptionalMetadataHasSafeDefaults()
    {
        Write("manifest.json", "{}");
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal("", info.Description);
        Assert.Equal("", info.Version);
        Assert.Equal("", info.IconDataUri);
        Assert.Null(info.PopupPath);
        Assert.Null(info.OptionsPath);
        Assert.Empty(info.Permissions);
    }

    [Fact]
    public void Read_SupportsLegacyActionAndOptionsFallbacks()
    {
        Write("legacy.html", "content");
        Write("legacy.png", Png);
        Manifest(new
        {
            action = new { default_popup = "missing.html" },
            browser_action = new { default_popup = "legacy.html", default_icon = new Dictionary<string, string> { ["32"] = "legacy.png" } },
            options_ui = new { page = "missing.html" }, options_page = "legacy.html"
        });
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal("legacy.html", info.PopupPath);
        Assert.Equal("legacy.html", info.OptionsPath);
        Assert.StartsWith("data:image/png;base64,", info.IconDataUri);
    }

    [Fact]
    public void Read_SupportsPageActionAndStringIcon()
    {
        Write("popup.html", "content");
        Write("image.png", Png);
        Manifest(new { page_action = new { default_popup = "popup.html", default_icon = "image.png" } });
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal("popup.html", info.PopupPath);
        Assert.StartsWith("data:image/png;base64,", info.IconDataUri);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("nested/../../outside.png")]
    [InlineData("C:\\outside.png")]
    [InlineData("\\\\server\\share\\outside.png")]
    [InlineData("/outside.png")]
    [InlineData("https://example.com/icon.png")]
    [InlineData("outside.png:stream")]
    [InlineData("%2e%2e/outside.png")]
    [InlineData("nested/../outside.png")]
    [InlineData("image.png?anything")]
    [InlineData("./image.png")]
    public void Read_RejectsUnsafeReferencedPaths(string path)
    {
        Write("image.png", Png);
        Manifest(new { icons = new Dictionary<string, string> { ["128"] = path }, action = new { default_popup = path }, options_page = path });
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal("", info.IconDataUri);
        Assert.Null(info.PopupPath);
        Assert.Null(info.OptionsPath);
    }

    [Theory]
    [InlineData("icon.svg", "<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("icon.png", "<script>alert(1)</script>")]
    [InlineData("icon.jpg", "not an image")]
    public void Read_IgnoresUnsupportedOrSpoofedIcons(string file, string content)
    {
        Write(file, content);
        Manifest(new { icons = new Dictionary<string, string> { ["128"] = file } });
        Assert.Equal("", ExtensionManifestReader.Read(_root).IconDataUri);
    }

    [Fact]
    public void Read_FallsBackWhenPreferredIconIsInvalid()
    {
        Write("bad.png", "not an image");
        Write("good.png", Png);
        Manifest(new { icons = new Dictionary<string, string> { ["256"] = "bad.png", ["128"] = "good.png" } });
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), ExtensionManifestReader.Read(_root).IconDataUri);
    }

    [Fact]
    public void Read_BoundsOptionalFilesAndFallsBack()
    {
        Write("large.png", new byte[1024 * 1024 + 1]);
        Write("_locales/fr/messages.json", new string(' ', 256 * 1024 + 1));
        Write("_locales/en/messages.json", """{"description":{"message":"Fallback"}}""");
        Manifest(new { description = "__MSG_description__", default_locale = "fr", icons = new Dictionary<string, string> { ["128"] = "large.png" } });
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal("", info.IconDataUri);
        Assert.Equal("Fallback", info.Description);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    public void Read_RejectsBrokenManifest(string text)
    {
        Write("manifest.json", text);
        Assert.Throws<InvalidDataException>(() => ExtensionManifestReader.Read(_root));
    }

    [Fact]
    public void Read_RejectsMissingAndOversizedManifest()
    {
        Assert.Throws<InvalidDataException>(() => ExtensionManifestReader.Read(_root));
        Write("manifest.json", new string(' ', 512 * 1024 + 1));
        Assert.Contains("size limit", Assert.Throws<InvalidDataException>(() => ExtensionManifestReader.Read(_root)).Message);
    }

    [Fact]
    public void Read_AcceptsUtf8BomAndWindowsRelativeSeparators()
    {
        Write("nested/options.html", "content");
        var json = JsonSerializer.Serialize(new { options_page = "nested\\options.html", description = "Metadata" });
        Write("manifest.json", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray());
        Assert.Equal("nested/options.html", ExtensionManifestReader.Read(_root).OptionsPath);
    }

    [Fact]
    public void Read_BoundsPermissionsAndSkipsNonStringOrControlValues()
    {
        Manifest(new { permissions = new object[] { 3, "tabs", "tabs", "", "bad\nvalue", new string('x', 513) }.Concat(Enumerable.Range(0, 200).Select(i => (object)$"permission-{i}")), host_permissions = new[] { "https://example.com/*" } });
        var info = ExtensionManifestReader.Read(_root);
        Assert.Equal(128, info.Permissions.Count);
        Assert.Equal("tabs", info.Permissions[0]);
        Assert.DoesNotContain("bad\nvalue", info.Permissions);
    }

    [Fact]
    public void Read_RejectsDirectoryJunctionReferencesWithoutReadingOrDeletingTarget()
    {
        var outside = Path.Combine(Path.GetTempPath(), "Stride-extension-outside-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(_root, "linked");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "icon.png"), Png);
        File.WriteAllText(Path.Combine(outside, "popup.html"), "keep");
        File.WriteAllText(Path.Combine(outside, "messages.json"), """{"description":{"message":"Outside"}}""");
        try
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(outside);
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(0, process.ExitCode);
            Manifest(new { icons = new Dictionary<string, string> { ["128"] = "linked/icon.png" }, action = new { default_popup = "linked/popup.html" }, options_page = "linked/popup.html" });
            var info = ExtensionManifestReader.Read(_root);
            Assert.Equal("", info.IconDataUri);
            Assert.Null(info.PopupPath);
            Assert.Null(info.OptionsPath);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "popup.html")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(outside, true);
        }
    }

    private void Manifest(object manifest) => Write("manifest.json", JsonSerializer.Serialize(manifest));
    private void Write(string path, string content) => Write(path, Encoding.UTF8.GetBytes(content));
    private void Write(string path, byte[] content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
