namespace StrideBrowser.Services;

/// <summary>
/// Pure domain-matching logic for Focus Mode blocklists. Extracted from
/// <see cref="FocusBlocklistService"/> so the matching rules are independently testable
/// without settings storage or network access.
/// </summary>
public static class FocusDomainMatcher
{
    /// <summary>True if <paramref name="host"/> equals or is a subdomain of any domain in <paramref name="customDomains"/>.</summary>
    public static bool MatchesCustomDomain(string host, IEnumerable<string> customDomains)
    {
        return customDomains.Any(d =>
            host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True if <paramref name="host"/> or any of its parent domains is present in <paramref name="blockedDomains"/>.
    /// e.g. for "m.images.badsite.com", checks "m.images.badsite.com", "images.badsite.com", "badsite.com".
    /// </summary>
    public static bool MatchesBlockedDomain(string host, IReadOnlySet<string> blockedDomains, out string? matchedSuffix)
    {
        var parts = host.Split('.');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var subHost = string.Join(".", parts.Skip(i));
            if (blockedDomains.Contains(subHost))
            {
                matchedSuffix = subHost;
                return true;
            }
        }

        matchedSuffix = null;
        return false;
    }
}
