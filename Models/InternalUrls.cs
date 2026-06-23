namespace SpurBrowser.Models;

/// <summary>Constants for internal browser URLs. Eliminates magic strings.</summary>
public static class InternalUrls
{
    public const string NewTab = "spur://newtab";
    public const string Settings = "spur://settings";
    public const string OneTab = "spur://onetab";
    public const string History = "spur://history";
    public const string Prefix = "spur://";

    public static bool IsInternal(string url) =>
        url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsDataOrBlank(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        url == "about:blank";
}
