namespace StrideBrowser.Models;

/// <summary>Constants for internal browser URLs. Eliminates magic strings.</summary>
public static class InternalUrls
{
    public const string NewTab = "internal://newtab";
    public const string Settings = "internal://settings";
    public const string OneTab = "internal://onetab";
    public const string History = "internal://history";
    public const string Downloads = "internal://downloads";
    public const string Onboarding = "internal://onboarding";
    public const string ReleaseNotes = "internal://releasenotes";
    public const string Focus = "internal://focus";
    public const string Prefix = "internal://";

    public static bool IsInternal(string url) =>
        url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        url == "about:blank";

    public static bool IsDataOrBlank(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        url == "about:blank";
}
