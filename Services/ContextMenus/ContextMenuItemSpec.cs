using System;

namespace StrideBrowser.Services.ContextMenus;

/// <summary>
/// One entry in a rendered context menu. A closed set: commands and separators.
/// The renderer maps a Command's Id to its behavior; the builder stays pure.
/// </summary>
public abstract record ContextMenuItemSpec
{
    private ContextMenuItemSpec() { }

    public sealed record Command(
        string Id,
        string Header,
        string GestureText = "",
        bool IsEnabled = true,
        string Payload = "") : ContextMenuItemSpec;

    public sealed record Separator : ContextMenuItemSpec
    {
        public static readonly Separator Instance = new();
        private Separator() { }
    }
}
