using System;
using System.Collections.Generic;

namespace StrideBrowser.Services.ContextMenus;

/// <summary>
/// Pure factory for context menu layouts. Takes a snapshot of the right-click
/// target and returns the full item plan; rendering and behavior live elsewhere.
///
/// The menu adapts to what the user clicked on:
///   Plain page  → navigation + find + select all + print + dev tools + dark mode
///   Link        → link actions + page globals
///   Image       → image actions + page globals (no view-source)
///   Selection   → copy + search + page globals
///   Editable    → undo/redo + clipboard + select all + inspect only
///   Link+Image  → link actions + image actions + page globals
/// </summary>
public static class ContextMenuPlanBuilder
{
    private const int SelectionPreviewLimit = 20;

    public static ContextMenuPlan Build(ContextMenuContext ctx, string searchEngine)
    {
        // Editable fields get a focused clipboard menu, nothing else.
        if (ctx.IsEditable)
            return BuildEditable(ctx);

        var items = new List<ContextMenuItemSpec>();

        // ── Contextual sections ──

        if (ctx.HasLink)
        {
            items.Add(Cmd("open-link-new-tab", "Open link in new tab", payload: ctx.LinkUri));
            items.Add(Cmd("copy-link", "Copy link address", payload: ctx.LinkUri));
            items.Add(Sep());
        }

        if (ctx.MediaKind == ContextMenuMediaKind.Image)
        {
            items.Add(Cmd("open-image-new-tab", "Open image in new tab", payload: ctx.SourceUri));
            items.Add(Cmd("save-image", "Save image as...", payload: ctx.SourceUri));
            items.Add(Cmd("copy-image-url", "Copy image URL", payload: ctx.SourceUri));
            items.Add(Sep());
        }

        if (ctx.HasSelection)
        {
            items.Add(Cmd("copy", "Copy"));
            items.Add(Cmd("search-selection",
                $"Search Stride for '{Truncate(ctx.SelectionText)}'",
                payload: BuildSearchUrl(ctx.SelectionText, searchEngine)));
            items.Add(Sep());
        }

        // ── Navigation items (plain page only, when no link/image/selection) ──

        if (!ctx.HasSelection)
        {
            items.Add(Cmd("select-all", "Select all"));
            items.Add(Sep());
        }

        // ── Page-level globals ──

        items.Add(Cmd("find-in-page", "Find in page", "Ctrl+F"));
        items.Add(Sep());

        // Always show reader toggle to match toolbar button behavior (availability is evaluated on click)
        items.Add(Cmd("toggle-reader",
            ctx.IsInReader ? "Exit reader view" : "Enter reader view", "Ctrl+Shift+R"));
        items.Add(Sep());

        items.Add(Cmd("launch-tc-lens", "T&C Lens", "Alt+T"));
        items.Add(Sep());

        items.Add(Cmd("print", "Print", "Ctrl+P"));
        items.Add(Sep());

        items.Add(Cmd("toggle-dark-mode",
            ctx.ForceDarkMode ? "Disable dark mode" : "Enable dark mode"));

        // View source only on page/link/selection contexts, not images
        if (ctx.MediaKind != ContextMenuMediaKind.Image)
            items.Add(Cmd("view-source", "View page source", "Ctrl+U"));

        items.Add(Cmd("inspect", "Inspect", "F12"));

        return new ContextMenuPlan
        {
            NavigationRow = new ContextMenuPlan.NavigationRowSpec(ctx.CanGoBack, ctx.CanGoForward),
            Items = items,
        };
    }

    private static ContextMenuPlan BuildEditable(ContextMenuContext ctx)
    {
        var items = new List<ContextMenuItemSpec>
        {
            Cmd("undo", "Undo", "Ctrl+Z"),
            Cmd("redo", "Redo", "Ctrl+Y"),
            Sep(),
            Cmd("cut", "Cut"),
            Cmd("copy", "Copy"),
            Cmd("paste", "Paste"),
            Cmd("select-all", "Select all"),
            Sep(),
            Cmd("inspect", "Inspect", "F12"),
        };

        return new ContextMenuPlan
        {
            NavigationRow = new ContextMenuPlan.NavigationRowSpec(ctx.CanGoBack, ctx.CanGoForward),
            Items = items,
        };
    }

    // ── Helpers ──

    internal static string BuildSearchUrl(string selectionText, string searchEngine)
    {
        var query = Uri.EscapeDataString(selectionText);
        return searchEngine switch
        {
            "Google" => $"https://www.google.com/search?q={query}",
            "Bing" => $"https://www.bing.com/search?q={query}",
            _ => $"https://duckduckgo.com/?q={query}",
        };
    }

    private static string Truncate(string text)
    {
        if (text.Length <= SelectionPreviewLimit) return text;
        return text[..(SelectionPreviewLimit - 3)] + "...";
    }

    private static ContextMenuItemSpec.Command Cmd(
        string id, string header, string gesture = "", bool isEnabled = true, string payload = "")
        => new(id, header, gesture, isEnabled, payload);

    private static ContextMenuItemSpec.Separator Sep()
        => ContextMenuItemSpec.Separator.Instance;
}
