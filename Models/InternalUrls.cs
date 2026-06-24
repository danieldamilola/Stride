namespace StrideBrowser.Models;

/// <summary>Constants for internal browser URLs. Eliminates magic strings.</summary>
public static class InternalUrls
{
    public const string NewTab = "stride://newtab";
    public const string Settings = "stride://settings";
    public const string OneTab = "stride://onetab";
    public const string History = "stride://history";
    public const string Prefix = "stride://";

    public static bool IsInternal(string url) =>
        url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsDataOrBlank(string url) =>
        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        url == "about:blank";
}
