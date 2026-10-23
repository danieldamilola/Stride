using UmberBrowser.Models;

namespace UmberBrowser.Services.Extensions;

public static class BuiltInExtensions
{
    public static bool Contains(string id) => id is "umber:unhook" or "umber:youtube" or "umber:tint" or "umber:focus" or "umber:dark";
    public static string SettingKey(string id) => id switch
    {
        "umber:unhook" => "unhookEnabled", "umber:youtube" => "ytEnhancerEnabled",
        "umber:tint" => "adaptiveTint", "umber:dark" => "darkMode",
        _ => throw new InvalidOperationException("Open Focus settings to configure permanent blocking.")
    };
    public static string Section(string id) => id switch
    {
        "umber:unhook" or "umber:youtube" => "section-youtube",
        "umber:focus" => "section-focus", "umber:tint" => "section-appearance",
        "umber:dark" => "section-privacy", _ => throw new InvalidOperationException("Unknown built-in extension.")
    };
    public static IReadOnlyList<InstalledExtension> List(BrowserSettings settings) =>
    [
        Item("unhook", "YouTube Unhook", "Hide distracting feeds, Shorts, comments and other YouTube sections.", settings.UnhookEnabled),
        Item("youtube", "YouTube Enhancer", "Control video quality, playback speed and autoplay.", settings.YtEnhancerEnabled),
        Item("tint", "Adaptive toolbar color", "Match the toolbar to the current website.", settings.AdaptiveToolbarTint),
        Item("focus", "Focus", "Block websites with permanent Focus rules. Configure and lock rules in Focus settings.", settings.FocusLocked, false),
        Item("dark", "Force dark websites", "Use a dark appearance on websites. Restart umber after changing this setting.", settings.ForceDarkMode)
    ];
    private static InstalledExtension Item(string id, string name, string description, bool enabled, bool canToggle = true) =>
        new("umber:" + id, name, description, "", enabled, true, false, "", false, true, [], "Built into umber", "", null, canToggle);
}
