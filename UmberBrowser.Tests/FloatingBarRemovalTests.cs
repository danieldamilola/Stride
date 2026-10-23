using System.IO;
using System.Text.Json;
using System.Xml;
using UmberBrowser.Helpers;
using UmberBrowser.Models;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// Guards the floating command bar removal. The standard toolbar address
/// field is the only address UI, so none of the floating overlay, setting,
/// IPC, or onboarding pieces may come back, and profiles saved while the
/// setting existed must still load.
/// </summary>
public sealed class FloatingBarRemovalTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Umber.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string ReadRoot(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative));

    [Fact]
    public void LegacySettingsFile_WithRemovedFloatingKey_LoadsWithoutIt()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var loaded = JsonSerializer.Deserialize<BrowserSettings>(
            "{\"useFloatingCommandBar\":true,\"addressBarOnLeft\":false}", opts);

        Assert.NotNull(loaded);
        Assert.False(loaded.AddressBarOnLeft);
        Assert.Null(typeof(BrowserSettings).GetProperty("UseFloatingCommandBar"));
    }

    [Fact]
    public void MainWindowXaml_HasNoFloatingElementsOrHandlers()
    {
        var xaml = ReadRoot("MainWindow.xaml");
        foreach (var token in new[]
        {
            "UseFloatingCommandBar", "CommandBarGrid", "CommandBarPanel",
            "=\"UrlLabel_Click\"", "=\"CommandBarBackdrop_Click\"",
            "=\"SuggestionsList_MouseUp\"",
            "=\"AddressBar_KeyDown\"", "=\"AddressBar_GotFocus\"",
            "=\"AddressBar_PreviewMouseDown\"", "=\"AddressBar_LostFocus\"",
            "=\"AddressBar_TextChanged\"", "CommandBarInputStyle",
            "x:Name=\"AddressBar\"", "x:Name=\"UrlLabel\"", "x:Name=\"SecurityIcon\""
        })
            Assert.DoesNotContain(token, xaml);
    }

    [Fact]
    public void NewTabPage_HasNoSearchField()
    {
        var html = ReadRoot("Resources/Pages/NewTab.html");
        Assert.DoesNotContain("search-container", html);
        Assert.DoesNotContain("search-input", html);
        Assert.DoesNotContain("Search the web or type a URL", html);
    }

    [Fact]
    public void TabFallback_UsesTheTransparentMark()
    {
#if DEBUG
        Assert.EndsWith("umber-mark-dev-light-32x32.png", AppIconResources.TabDark);
        Assert.EndsWith("umber-mark-dev-dark-32x32.png", AppIconResources.TabLight);
#else
        Assert.EndsWith("umber-mark-light-32x32.png", AppIconResources.TabDark);
        Assert.EndsWith("umber-mark-dark-32x32.png", AppIconResources.TabLight);
#endif
    }

    [Fact]
    public void MainWindowXaml_KeepsDomainDisplayAddressField()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(RepoRoot(), "MainWindow.xaml"));
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        ns.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml");

        var border = doc.SelectSingleNode("//*[@x:Name='StandardAddressBarBorder']", ns);
        Assert.NotNull(border);
        Assert.Equal("200", border.Attributes?["Width"]?.Value);

        var box = doc.SelectSingleNode("//*[@x:Name='StandardAddressBar']", ns);
        Assert.NotNull(box);
        Assert.Contains("AddressText", box.Attributes?["Text"]?.Value);
        Assert.Equal("StandardAddressBar_GotKeyboardFocus", box.Attributes?["GotKeyboardFocus"]?.Value);

        var popup = doc.SelectSingleNode("//*[@x:Name='StandardSuggestionsPopup']", ns);
        Assert.NotNull(popup);
        Assert.Contains("StandardPopupOpen", popup.Attributes?["IsOpen"]?.Value);

        var popupBorder = doc.SelectSingleNode("//*[@x:Name='StandardSuggestionsPopupBorder']", ns);
        Assert.NotNull(popupBorder);
    }

    [Fact]
    public void MainWindowXaml_AddressFieldDrawsEmptyHintInsteadOfRealText()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(RepoRoot(), "MainWindow.xaml"));
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        ns.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml");

        var box = doc.SelectSingleNode("//w:TextBox[@x:Name='StandardAddressBar']", ns);
        Assert.NotNull(box);

        // AddressBarStyle repaints the field and breaks the single 38 DIP row.
        // The standard field keeps its own template instead.
        Assert.Null(box.Attributes?["Style"]);

        var hint = box.SelectSingleNode(".//w:TextBlock[@x:Name='AddressHint']", ns);
        Assert.NotNull(hint);
        Assert.Equal("Search the web or type a URL", hint.Attributes?["Text"]?.Value);
        Assert.Equal("False", hint.Attributes?["IsHitTestVisible"]?.Value);

        // Hint and content host take the same bound margin, so both text
        // origins land on one pixel without assuming what Padding is. The
        // hint adds the measured 2 DIP content host inset on top of that.
        var host = box.SelectSingleNode(".//*[@x:Name='PART_ContentHost']", ns);
        Assert.NotNull(host);
        Assert.Contains("TemplateBinding Padding", host.Attributes?["Margin"]?.Value ?? "");
        Assert.Equal("2,0,0,0", hint.Attributes?["Margin"]?.Value);

        // The inset is only valid while the box itself carries no padding of
        // its own, so a later Padding would silently misalign the hint.
        Assert.Null(box.Attributes?["Padding"]);

        // The hint keys on empty text alone. It must survive focus and clear
        // as soon as anything is typed, so no Condition may join the trigger.
        var setter = box.SelectSingleNode(
            ".//*[local-name()='Trigger' and @Property='Text' and @Value='']" +
            "/*[local-name()='Setter' and @TargetName='AddressHint'" +
            " and @Property='Visibility' and @Value='Visible']", ns);
        Assert.NotNull(setter);
        Assert.Empty(setter!.SelectNodes(".//*[local-name()='Condition']")!);
    }

    [Fact]
    public void AppXaml_HasNoFloatingInputStyle()
    {
        var xaml = ReadRoot("App.xaml");
        Assert.DoesNotContain("CommandBarInputStyle", xaml);
        Assert.Contains("FaviconTabListStyle", xaml);
    }

    [Fact]
    public void SettingsPage_HasNoFloatingToggle()
    {
        var html = ReadRoot("Resources/Pages/Settings.html");
        Assert.DoesNotContain("useFloatingBar", html);
        Assert.DoesNotContain("CHK_USE_FLOATING_BAR", html);
        Assert.Contains("adaptiveTint", html);
    }

    [Fact]
    public void OnboardingPage_HasNoFloatingChoice()
    {
        var html = ReadRoot("Resources/Pages/Onboarding.html");
        Assert.DoesNotContain("useFloatingBar", html);
        Assert.DoesNotContain("floatingBar", html);
        Assert.DoesNotContain("addrFloating", html);
        Assert.Contains("addrStandard", html);
    }

    [Fact]
    public void HandlerSources_HaveNoFloatingWiring()
    {
        Assert.DoesNotContain("floatingBar", ReadRoot("Services/Pages/OnboardingPage.cs"));
        Assert.DoesNotContain("floatingBar", ReadRoot("Engine/Handlers/OnboardingMessageHandler.cs"));
        Assert.DoesNotContain("useFloatingBar", ReadRoot("Engine/Handlers/SettingsMessageHandler.cs"));
    }

    [Fact]
    public void MainWindowXaml_EveryHandlerExistsInCodeBehind()
    {
        var xaml = ReadRoot("MainWindow.xaml");
        var behind = ReadRoot("MainWindow.xaml.cs");
        var handlers = System.Text.RegularExpressions.Regex.Matches(
            xaml, "(Click|KeyDown|TextChanged|MouseUp|MouseDown|MouseLeave|GotKeyboardFocus|LostFocus|GotFocus|SelectionChanged|PreviewMouseWheel|PreviewMouseDown|PreviewKeyDown|PreviewMouseLeftButtonDown|SizeChanged|RequestBringIntoView)=\"([A-Za-z_][A-Za-z0-9_]*)\"");
        Assert.NotEmpty(handlers);
        foreach (System.Text.RegularExpressions.Match m in handlers)
            Assert.Contains("void " + m.Groups[2].Value + "(", behind);
    }
}
