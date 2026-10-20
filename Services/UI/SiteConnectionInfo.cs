namespace StrideBrowser.Services.UI;

public sealed record SiteConnectionInfo(string Host, bool IsInsecure, string Summary, string Description)
{
    public static SiteConnectionInfo? FromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return null;

        var host = NavigationService.ToSafeAsciiHost(uri);
        if (!uri.IsDefaultPort) host += ":" + uri.Port;

        return uri.Scheme == "http"
            ? new(host, true, "Connection is not encrypted",
                "Information sent to this HTTP address can be read or changed by others on the network.")
            : new(host, false, "HTTPS connection",
                "HTTPS protects information in transit when the connection is valid. It does not establish whether a site is trustworthy.");
    }
}
