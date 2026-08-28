using System.Linq;
using StrideBrowser.Services.ContextMenus;
using Xunit;

namespace StrideBrowser.Tests;

public class ContextMenuPlanBuilderTests
{
    private static ContextMenuContext PageContext() => new()
    {
        CanGoBack = false,
        CanGoForward = false,
        IsEditable = false,
        ForceDarkMode = false,
        IsReaderAvailable = false,
        IsInReader = false
    };

    private static string? Header(ContextMenuPlan plan, string id) =>
        plan.Items.OfType<ContextMenuItemSpec.Command>().FirstOrDefault(c => c.Id == id)?.Header;

    private static string? Gesture(ContextMenuPlan plan, string id) =>
        plan.Items.OfType<ContextMenuItemSpec.Command>().FirstOrDefault(c => c.Id == id)?.GestureText;

    private static bool HasCommand(ContextMenuPlan plan, string id) =>
        plan.Items.OfType<ContextMenuItemSpec.Command>().Any(c => c.Id == id);

    // ── Plain page context ──

    [Fact]
    public void PlainPage_ShowsNavigationAndPageGlobals()
    {
        var plan = ContextMenuPlanBuilder.Build(PageContext(), "DuckDuckGo");

        Assert.NotNull(plan.NavigationRow);
        Assert.False(plan.NavigationRow!.CanGoBack);
        Assert.False(plan.NavigationRow.CanGoForward);

        
        
        
        

        // Page tools
        Assert.True(HasCommand(plan, "find-in-page"));
        Assert.True(HasCommand(plan, "select-all"));
        Assert.True(HasCommand(plan, "launch-tc-lens"));
        Assert.True(HasCommand(plan, "print"));
        Assert.True(HasCommand(plan, "view-source"));
        Assert.True(HasCommand(plan, "inspect"));
        Assert.True(HasCommand(plan, "toggle-dark-mode"));

        // Nothing contextual
        Assert.False(HasCommand(plan, "copy-link"));
        Assert.False(HasCommand(plan, "copy-image-url"));
        Assert.False(HasCommand(plan, "paste"));
        Assert.False(HasCommand(plan, "search-selection"));
        Assert.False(HasCommand(plan, "undo"));
    }

    [Fact]
    public void PlainPage_BackForwardReflectHistoryState()
    {
        var ctx = PageContext() with { CanGoBack = true, CanGoForward = true };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        
        
        Assert.True(plan.NavigationRow!.CanGoBack);
        Assert.True(plan.NavigationRow!.CanGoForward);
    }

    [Fact]
    public void PlainPage_BackForwardDisabledWhenNoHistory()
    {
        var plan = ContextMenuPlanBuilder.Build(PageContext(), "");

        
        
        Assert.False(plan.NavigationRow!.CanGoBack);
        Assert.False(plan.NavigationRow!.CanGoForward);
    }

    // ── Reader Context ──

    [Fact]
    public void Reader_WhenNotActive_ShowsEnterReader()
    {
        var plan = ContextMenuPlanBuilder.Build(PageContext(), "");
        Assert.True(HasCommand(plan, "toggle-reader"));
        Assert.Equal("Enter reader view", Header(plan, "toggle-reader"));
        Assert.Equal("Ctrl+Shift+R", Gesture(plan, "toggle-reader"));
    }

    [Fact]
    public void Reader_WhenActive_ShowsExitReader()
    {
        var plan = ContextMenuPlanBuilder.Build(PageContext() with { IsInReader = true }, "");
        Assert.True(HasCommand(plan, "toggle-reader"));
        Assert.Equal("Exit reader view", Header(plan, "toggle-reader"));
    }

    // ── Keyboard shortcuts shown ──

    [Fact]
    public void PlainPage_ItemsShowKeyboardShortcuts()
    {
        var plan = ContextMenuPlanBuilder.Build(PageContext(), "");

        
        
        
        Assert.Equal("Ctrl+F", Gesture(plan, "find-in-page"));
        Assert.Equal("Alt+T", Gesture(plan, "launch-tc-lens"));
        Assert.Equal("Ctrl+P", Gesture(plan, "print"));
        Assert.Equal("Ctrl+U", Gesture(plan, "view-source"));
        Assert.Equal("F12", Gesture(plan, "inspect"));
    }

    // ── Link context ──

    [Fact]
    public void LinkContext_AddsLinkItemsCarryingTheUri()
    {
        var ctx = PageContext() with { HasLink = true, LinkUri = "https://example.com/a" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        var open = plan.Items.OfType<ContextMenuItemSpec.Command>().First(c => c.Id == "open-link-new-tab");
        var copy = plan.Items.OfType<ContextMenuItemSpec.Command>().First(c => c.Id == "copy-link");
        Assert.Equal("https://example.com/a", open.Payload);
        Assert.Equal("https://example.com/a", copy.Payload);
    }

    [Fact]
    public void LinkContext_NoNavigationTextItems()
    {
        var ctx = PageContext() with { HasLink = true, LinkUri = "https://example.com" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.False(HasCommand(plan, "back"));
        Assert.False(HasCommand(plan, "forward"));
        Assert.False(HasCommand(plan, "reload"));
        Assert.True(HasCommand(plan, "find-in-page"));
    }

    [Fact]
    public void LinkContext_StillHasSelectAllAndPageGlobals()
    {
        var ctx = PageContext() with { HasLink = true, LinkUri = "https://example.com" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "select-all"));
        Assert.True(HasCommand(plan, "print"));
        Assert.True(HasCommand(plan, "view-source"));
        Assert.True(HasCommand(plan, "inspect"));
    }

    // ── Image context ──

    [Fact]
    public void ImageContext_AddsImageItemsIncludingSave()
    {
        var ctx = PageContext() with { MediaKind = ContextMenuMediaKind.Image, SourceUri = "https://example.com/i.png" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "open-image-new-tab"));
        Assert.True(HasCommand(plan, "copy-image-url"));
        Assert.True(HasCommand(plan, "save-image"));
        Assert.Equal("https://example.com/i.png",
            plan.Items.OfType<ContextMenuItemSpec.Command>().First(c => c.Id == "save-image").Payload);
    }

    [Fact]
    public void ImageContext_NoViewSource()
    {
        var ctx = PageContext() with { MediaKind = ContextMenuMediaKind.Image, SourceUri = "https://example.com/i.png" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.False(HasCommand(plan, "view-source"));
        Assert.True(HasCommand(plan, "inspect"));
    }

    // ── Selection context ──

    [Fact]
    public void SelectionContext_ShowsCopyAndSearch()
    {
        var ctx = PageContext() with { HasSelection = true, SelectionText = "hello" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "copy"));
        Assert.True(HasCommand(plan, "search-selection"));
        Assert.False(HasCommand(plan, "back"));
        Assert.True(HasCommand(plan, "find-in-page"));
    }

    [Fact]
    public void SelectionContext_NoSelectAll()
    {
        var ctx = PageContext() with { HasSelection = true, SelectionText = "hello" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        // When text is selected, Select all is redundant
        Assert.False(HasCommand(plan, "select-all"));
    }

    [Fact]
    public void SelectionContext_SearchHeaderTruncatesLongText()
    {
        const string longText = "abcdefghijklmnopqrstuvwxyz";
        var ctx = PageContext() with { HasSelection = true, SelectionText = longText };

        Assert.Equal("Search Stride for 'abcdefghijklmnopq...'",
            Header(ContextMenuPlanBuilder.Build(ctx, ""), "search-selection"));
    }

    [Fact]
    public void SelectionContext_ShortTextIsShownInFull()
    {
        var ctx = PageContext() with { HasSelection = true, SelectionText = "hello world" };

        Assert.Equal("Search Stride for 'hello world'",
            Header(ContextMenuPlanBuilder.Build(ctx, ""), "search-selection"));
    }

    // ── Editable context ──

    [Fact]
    public void EditableContext_ShowsClipboardAndUndoRedo()
    {
        var ctx = PageContext() with { IsEditable = true };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "undo"));
        Assert.True(HasCommand(plan, "redo"));
        Assert.True(HasCommand(plan, "cut"));
        Assert.True(HasCommand(plan, "copy"));
        Assert.True(HasCommand(plan, "paste"));
        Assert.True(HasCommand(plan, "select-all"));
        Assert.True(HasCommand(plan, "inspect"));
    }

    [Fact]
    public void EditableContext_NoPrintOrViewSource()
    {
        var ctx = PageContext() with { IsEditable = true };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.False(HasCommand(plan, "print"));
        Assert.False(HasCommand(plan, "view-source"));
        Assert.False(HasCommand(plan, "toggle-dark-mode"));
        Assert.False(HasCommand(plan, "back"));
        Assert.False(HasCommand(plan, "find-in-page"));
    }

    [Fact]
    public void EditableContext_WinsOverSelection()
    {
        var ctx = PageContext() with { IsEditable = true, HasSelection = true, SelectionText = "selected words" };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "cut"));
        Assert.True(HasCommand(plan, "paste"));
        Assert.True(HasCommand(plan, "undo"));
        Assert.False(HasCommand(plan, "search-selection"));
    }

    [Fact]
    public void EditableContext_ShowsShortcuts()
    {
        var ctx = PageContext() with { IsEditable = true };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.Equal("Ctrl+Z", Gesture(plan, "undo"));
        Assert.Equal("Ctrl+Y", Gesture(plan, "redo"));
        Assert.Equal("F12", Gesture(plan, "inspect"));
    }

    // ── Link + Image (image wrapped in a link) ──

    [Fact]
    public void LinkPlusImage_ShowsBothSections()
    {
        var ctx = PageContext() with
        {
            HasLink = true, LinkUri = "https://example.com/page",
            MediaKind = ContextMenuMediaKind.Image, SourceUri = "https://example.com/img.jpg",
        };
        var plan = ContextMenuPlanBuilder.Build(ctx, "");

        Assert.True(HasCommand(plan, "open-link-new-tab"));
        Assert.True(HasCommand(plan, "copy-link"));
        Assert.True(HasCommand(plan, "open-image-new-tab"));
        Assert.True(HasCommand(plan, "save-image"));
        Assert.True(HasCommand(plan, "copy-image-url"));
        Assert.False(HasCommand(plan, "view-source"));
    }

    // ── Dark mode toggle ──

    [Fact]
    public void DarkModeToggle_LabelDependsOnCurrentState()
    {
        Assert.Equal("Enable dark mode",
            Header(ContextMenuPlanBuilder.Build(PageContext(), ""), "toggle-dark-mode"));

        var dark = PageContext() with { ForceDarkMode = true };
        Assert.Equal("Disable dark mode",
            Header(ContextMenuPlanBuilder.Build(dark, ""), "toggle-dark-mode"));
    }

    // ── Navigation row ──

    [Fact]
    public void NavigationRow_ReflectsHistoryState()
    {
        var ctx = PageContext() with { CanGoBack = true, CanGoForward = true };
        var row = ContextMenuPlanBuilder.Build(ctx, "").NavigationRow!;

        Assert.True(row.CanGoBack);
        Assert.True(row.CanGoForward);
    }

    // ── Search engine routing ──

    [Theory]
    [InlineData("Google", "https://www.google.com/search?q=hello")]
    [InlineData("Bing", "https://www.bing.com/search?q=hello")]
    [InlineData("DuckDuckGo", "https://duckduckgo.com/?q=hello")]
    [InlineData("", "https://duckduckgo.com/?q=hello")]
    public void SearchSelection_PayloadRespectsSearchEngine(string engine, string expectedUrl)
    {
        var ctx = PageContext() with { HasSelection = true, SelectionText = "hello" };
        var plan = ContextMenuPlanBuilder.Build(ctx, engine);

        Assert.Equal(expectedUrl,
            plan.Items.OfType<ContextMenuItemSpec.Command>().First(c => c.Id == "search-selection").Payload);
    }

    // ── Structural invariant ──

    [Fact]
    public void Plan_NeverEndsWithSeparator()
    {
        var contexts = new[]
        {
            PageContext(),
            PageContext() with { HasLink = true },
            PageContext() with { MediaKind = ContextMenuMediaKind.Image },
            PageContext() with { IsEditable = true },
            PageContext() with { HasSelection = true, SelectionText = "x" },
            PageContext() with { HasLink = true, MediaKind = ContextMenuMediaKind.Image },
        };

        foreach (var ctx in contexts)
        {
            var plan = ContextMenuPlanBuilder.Build(ctx, "");
            var last = plan.Items[^1];
            Assert.IsNotType<ContextMenuItemSpec.Separator>(last);
        }
    }
}

