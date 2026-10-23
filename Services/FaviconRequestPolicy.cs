using System.Globalization;
using System.Net;

namespace UmberBrowser.Services;

/// <summary>The answer the loader acts on. <see cref="Reason"/> is for the trace and tests.</summary>
public readonly record struct FaviconRequestDecision(bool Allowed, string Host, string Reason)
{
    internal static FaviconRequestDecision Denied(string reason) => new(false, string.Empty, reason);
}

/// <summary>
/// Decides whether a host may be sent to the third-party icon service.
///
/// The policy is deliberately conservative: it refuses anything it cannot show is a
/// normal public domain name, rather than trying to show that a name is public. There is
/// no DNS lookup, because a lookup would have to send the name somewhere to resolve it.
/// A name that passes is a name whose whole point is to be looked up by a public server,
/// so refusing the ambiguous cases costs a few blank icons and buys not publishing a
/// private host.
///
/// Nothing here touches the network and nothing here sees the cache. It is pure logic so
/// the decision can be tested on its own, and it is also the gate the loader re-runs at
/// the moment it would send a request.
/// </summary>
public static class FaviconRequestPolicy
{
    /// <summary>The only third-party icon service Umber talks to.</summary>
    public const string IconServiceBaseUrl = "https://icons.duckduckgo.com/ip3/";

    /// <summary>
    /// Suffixes that name something on the machine, the local network, or a reserved
    /// space rather than a site on the internet. Each entry carries the reserved tree's
    /// suffix and the bare apex, because a plain EndsWith on the suffix would let a two
    /// label apex such as home.arpa through. "localhost" is covered by the label check in
    /// <see cref="EvaluateHost"/> as well.
    /// </summary>
    private static readonly (string Suffix, string Apex)[] NonPublicSuffixes =
    {
        (".localhost", "localhost"),
        (".local", "local"),
        (".localdomain", "localdomain"),
        (".home.arpa", "home.arpa"),
        (".internal", "internal"),
        (".intranet", "intranet"),
        (".lan", "lan"),
        (".corp", "corp"),
        (".home", "home"),
        (".private", "private"),
        (".test", "test"),
        (".example", "example"),
        (".invalid", "invalid"),
        (".onion", "onion"),
    };

    /// <summary>
    /// Decides for a whole URL. Rejects anything Uri cannot read as an absolute web URL
    /// with a host, which covers opaque forms such as about:blank, data:, javascript:
    /// and file:, and rejects a bare hostname that was never a URL.
    ///
    /// This is the send gate. Production calls it with the parsed URL at the moment it is
    /// about to make a request, so the scheme it decides on is the scheme the user
    /// actually navigated to.
    /// </summary>
    public static FaviconRequestDecision Evaluate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return FaviconRequestDecision.Denied("no url");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return FaviconRequestDecision.Denied("not an absolute url");

        return Evaluate(uri);
    }

    /// <summary>The same decision for a URL the caller has already parsed.</summary>
    public static FaviconRequestDecision Evaluate(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return FaviconRequestDecision.Denied("not an absolute url");

        // Uri reports an empty host for opaque URIs, so this also covers the non-web
        // schemes that carry no authority at all.
        if (string.IsNullOrEmpty(uri.Host))
            return FaviconRequestDecision.Denied("no host in the url");

        // A non-web scheme with an authority, such as file://host/share, names a path on
        // this machine rather than a site. Its hostname is not something the icon service
        // needs, so it is refused here rather than after the host has been extracted and
        // the scheme discarded.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return FaviconRequestDecision.Denied($"scheme '{uri.Scheme}' is not http or https");

        return EvaluateHost(uri.Host);
    }

    /// <summary>
    /// Decides for a host that came out of <see cref="System.Uri.Host"/> already. The
    /// same normalization runs here as in <see cref="Evaluate"/>, so the loader can call
    /// this at the send boundary using the host it is about to look up.
    /// </summary>
    public static FaviconRequestDecision EvaluateHost(string? rawHost)
    {
        if (string.IsNullOrWhiteSpace(rawHost))
            return FaviconRequestDecision.Denied("no host");

        var host = NormalizeHost(rawHost);
        if (host is null)
            return FaviconRequestDecision.Denied("host could not be normalized to a domain name");

        // Every IP literal is refused, routable or not. Proving a public address is
        // public needs routing data this app does not have, and a private address sent
        // to a third party names the shape of someone's network.
        if (IsIpLiteral(host))
            return FaviconRequestDecision.Denied($"'{host}' is an ip literal");

        // A single label is a machine name on a local network, such as a router or a
        // file share, and there is no such thing as one on the public internet.
        if (!host.Contains('.'))
            return FaviconRequestDecision.Denied($"'{host}' is a single label intranet name");

        foreach (var label in host.Split('.'))
        {
            if (label.Length == 0)
                return FaviconRequestDecision.Denied($"'{host}' has an empty label");
            if (string.Equals(label, "localhost", StringComparison.Ordinal))
                return FaviconRequestDecision.Denied($"'{host}' names localhost");
        }

        foreach (var (suffix, apex) in NonPublicSuffixes)
        {
            // Both the name itself and anything under it. The bare forms of the single
            // label entries are already refused by the single label check, but home.arpa
            // is a two label reserved tree and its apex is the one name that would
            // otherwise slip past a plain EndsWith. A lookalike such as myhome.arpa is a
            // different name and stays allowed, which is what matching on a label
            // boundary rather than on a substring gives.
            if (host.EndsWith(suffix, StringComparison.Ordinal)
                || string.Equals(host, apex, StringComparison.Ordinal))
                return FaviconRequestDecision.Denied($"'{host}' is in the non-public '{suffix}' tree");
        }

        // A host that survives all of this is a plain lower-case ASCII domain name. The
        // request URL is built from this string, so the check also guarantees nothing
        // outside the letters, digits, dots and hyphens a hostname may use can reach the
        // request path.
        return new FaviconRequestDecision(true, host, "public host shape");
    }

    /// <summary>The icon service URL for a host that <see cref="EvaluateHost"/> allowed.</summary>
    public static string BuildIconUrl(string allowedHost) => IconServiceBaseUrl + allowedHost + ".ico";

    /// <summary>
    /// Lower cases, drops trailing dots and IPv6 brackets and zone ids, and returns null
    /// for anything that is not a plain ASCII domain name. Trailing dots are dropped
    /// because "example.com." names the same site as "example.com" and must not get a
    /// different answer.
    /// </summary>
    private static string? NormalizeHost(string rawHost)
    {
        var host = rawHost.Trim();

        // Uri brackets an IPv6 host. Unwrap it, then drop any zone id such as "%12" so
        // the literal check below sees the address itself.
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        var zone = host.IndexOf('%');
        if (zone >= 0) host = host[..zone];

        host = host.TrimEnd('.');
        if (host.Length == 0) return null;

        // IdnMapping converts an internationalized name to its ASCII form. Uri.Host is
        // already ASCII for an ordinary name, and a punycode label passes through
        // unchanged, so this only has work to do for an IDN the caller built by hand.
        if (!IsAsciiDomain(host))
        {
            try { host = new IdnMapping().GetAscii(host); }
            catch (ArgumentException) { return null; }
        }

        host = host.ToLowerInvariant();
        return IsAsciiDomain(host) ? host : null;
    }

    private static bool IsAsciiDomain(string host)
    {
        foreach (var c in host)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>
    /// True for an address in any form. IPAddress.TryParse covers the dotted-quad and
    /// IPv6 forms; the extra tests catch the shapes it does not, so an address cannot slip
    /// through as a name.
    /// </summary>
    private static bool IsIpLiteral(string host)
    {
        if (IPAddress.TryParse(host, out _)) return true;

        // An IPv4-in-IPv6 or zone bearing form that TryParse declined.
        if (host.Contains(':')) return true;

        // A host whose last label is entirely digits is an address someone wrote without
        // the dots being meaningful, or a truncated address. Neither is a domain name.
        var lastDot = host.LastIndexOf('.');
        var lastLabel = lastDot >= 0 ? host[(lastDot + 1)..] : host;
        return lastLabel.Length > 0 && lastLabel.All(char.IsDigit);
    }
}