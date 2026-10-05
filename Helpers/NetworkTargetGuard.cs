using System.Net;

namespace StrideBrowser.Helpers;

/// <summary>
/// Decides whether a URL's host is a network target the browser should not fetch on behalf of
/// remote content.
/// <para>
/// Two callers, two threat models. Reader mode pulls <c>img src</c> values out of an arbitrary
/// page, so a page can aim the renderer at the user's own network. Link preview is opened by an
/// Alt+click, so the user chose the target and previewing a LAN address is legitimate. Both
/// therefore reject loopback, link-local (which covers the 169.254.169.254 cloud metadata
/// service), multicast and the unspecified address. Reader mode additionally rejects RFC1918
/// private ranges because nothing in an article legitimately points at one.
/// </para>
/// <para>
/// A hostname that resolves to a private address is not caught here. The fetch happens inside
/// WebView2 and this code never sees the resolved address, so a hostile page can still aim a
/// public-looking hostname at 127.0.0.1. Closing that needs resolution inside the fetch path
/// rather than a check on the URL string.
/// </para>
/// </summary>
public static class NetworkTargetGuard
{
    /// <summary>
    /// Loopback and the local interfaces an article or preview must never reach.
    /// </summary>
    public static bool IsForbiddenHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        if (IsLoopbackName(host)) return true;

        // mDNS and single-label names resolve on the local network.
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Strip the brackets WebView2 and Uri put around a literal IPv6 address.
        var bare = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;

        if (!IPAddress.TryParse(bare, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        // Multicast, both families. IPAddress exposes IsIPv6Multicast only, so the IPv4
        // check is done on the octets: 224.0.0.0/4.
        if (address.IsIPv6Multicast) return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            // 169.254.0.0/16 link-local, including cloud metadata at 169.254.169.254
            if (b[0] == 169 && b[1] == 254) return true;
            // 224.0.0.0/4 multicast
            if (b[0] >= 224 && b[0] <= 239) return true;
        }
        else
        {
            if (address.IsIPv6LinkLocal) return true;
            // fc00::/7 unique local, the IPv6 counterpart of RFC1918
            if ((address.GetAddressBytes()[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }

    /// <summary>
    /// As <see cref="IsForbiddenHost"/>, and also rejects RFC1918 and IPv6 unique-local
    /// addresses. Used where the target is chosen by page content rather than the user.
    /// </summary>
    public static bool IsPrivateHost(string? host)
    {
        if (IsForbiddenHost(host)) return true;
        if (string.IsNullOrWhiteSpace(host)) return true;

        var bare = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        if (!IPAddress.TryParse(bare, out var address)) return false;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            // 10/8, 172.16/12, 192.168/16
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            // 100.64.0.0/10 carrier grade NAT, also not a public destination
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
        }

        return false;
    }

    private static bool IsLoopbackName(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a URL that the browser will fetch on behalf of page content should be refused.
    /// </summary>
    public static bool RejectsPageSuppliedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return true;
        if (uri.Scheme is not ("http" or "https")) return true;
        return IsPrivateHost(uri.Host);
    }

    /// <summary>
    /// True when a link preview may navigate to the URL. Used both by the preview policy and at
    /// the moment the preview fetches, so the decision does not depend on every caller routing
    /// through the policy. RFC1918 stays allowed because the user opened the preview on purpose.
    /// </summary>
    public static bool IsSafePreviewTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        return !IsForbiddenHost(uri.Host);
    }
}