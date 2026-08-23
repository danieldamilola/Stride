using System;

namespace StrideBrowser.Services.ContextMenus;

/// <summary>What the user right-clicked on.</summary>
public enum ContextMenuMediaKind
{
    Page,
    Image,
    Video,
    Audio,
}

/// <summary>
/// Snapshot of everything a context menu needs to know about the right-click target.
/// Built at the WebView2 boundary; string properties are pre-guarded there because
/// reading them unguarded throws COMException.
/// </summary>
public sealed record ContextMenuContext
{
    public required bool CanGoBack { get; init; }
    public required bool CanGoForward { get; init; }

    public bool HasLink { get; init; }
    public string LinkUri { get; init; } = "";

    public ContextMenuMediaKind MediaKind { get; init; } = ContextMenuMediaKind.Page;
    public string SourceUri { get; init; } = "";

    public required bool IsEditable { get; init; }
    public bool HasSelection { get; init; }
    public string SelectionText { get; init; } = "";

    /// <summary>Whether dark-mode forcing is currently on, so the toggle shows the right label.</summary>
    public required bool ForceDarkMode { get; init; }

    public required bool IsReaderAvailable { get; init; }
    public required bool IsInReader { get; init; }
}
