using System;
using System.Collections.Generic;

namespace StrideBrowser.Services.ContextMenus;

/// <summary>The complete layout of one context menu, ready to render.</summary>
public sealed record ContextMenuPlan
{
    public static readonly ContextMenuPlan Empty = new() { NavigationRow = null, Items = [] };

    /// <summary>The large Back / Forward / Refresh icon row shown at the top of the menu.</summary>
    public required NavigationRowSpec? NavigationRow { get; init; }

    public required IReadOnlyList<ContextMenuItemSpec> Items { get; init; }

    /// <summary>Enabled states for the three navigation buttons in the action row.</summary>
    public sealed record NavigationRowSpec(bool CanGoBack, bool CanGoForward);
}
