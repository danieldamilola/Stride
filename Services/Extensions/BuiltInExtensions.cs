using StrideBrowser.Models;

namespace StrideBrowser.Services.Extensions;

public static class BuiltInExtensions
{
    public static bool Contains(string id) => id is "stride:unhook" or "stride:youtube" or "stride:tint" or "stride:focus" or "stride:dark";
    public static string SettingKey(string id) => id switch
    {
        "stride:unhook" => "unhookEnabled", "stride:youtube" => "ytEnhancerEnabled",
        "stride:tint" => "adaptiveTint", "stride:dark" => "darkMode",
        _ => throw new InvalidOperationException("Open Focus settings to configure permanent blocking.")
    };
    public static string Section(string id) => id switch
    {
        "stride:unhook" or "stride:youtube" => "section-youtube",
        "stride:focus" => "section-focus", "stride:tint" => "section-appearance",
        "stride:dark" => "section-privacy", _ => throw new InvalidOperationException("Unknown built-in extension.")
    };
    public static IReadOnlyList<InstalledExtension> List(BrowserSettings settings) =>
    [
        Item("unhook", "YouTube Unhook", "Hide distracting feeds, Shorts, comments and other YouTube sections.", settings.UnhookEnabled),
        Item("youtube", "YouTube Enhancer", "Control video quality, playback speed and autoplay.", settings.YtEnhancerEnabled),
        Item("tint", "Adaptive toolbar color", "Match the toolbar to the current website.", settings.AdaptiveToolbarTint),
        Item("focus", "Focus", "Block websites with permanent Focus rules. Configure and lock rules in Focus settings.", settings.FocusLocked, false),
        Item("dark", "Force dark websites", "Use a dark appearance on websites. Restart Stride after changing this setting.", settings.ForceDarkMode)
    ];
    private static InstalledExtension Item(string id, string name, string description, bool enabled, bool canToggle = true) =>
        new("stride:" + id, name, description, "", enabled, true, false, "", false, true, [], "Built into Stride", "", null, canToggle);
}
