using System.Text.Json.Serialization;
using StrideBrowser.Services;

namespace StrideBrowser.Models;

/// <summary>
/// One tab the user closed, kept so it can be reopened with Ctrl+Shift+T.
/// Distinct from <see cref="SessionEntry"/>, which is the snapshot of the tabs
/// that were open when the app last exited. The snapshot says what to bring back
/// on startup; this record says what the user closed, in the order they closed
/// it, so the newest closure is always the next Ctrl+Shift+T.
/// </summary>
public sealed record ClosedTabRecord(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("wasPinned")] bool WasPinned,
    [property: JsonPropertyName("closedAtUtc")] DateTime ClosedAtUtc)
{
    /// <summary>
    /// Schemes a reopened tab can actually navigate to. Anything else cannot
    /// produce the tab the user closed.
    /// </summary>
    private static readonly string[] ReopenableSchemes = ["http", "https", "file", "internal", "about"];

    /// <summary>
    /// Normalises a candidate closure, or returns null when it could not be
    /// reopened.
    ///
    /// The URL is carried through whole. It is never clipped, because a clipped
    /// URL navigates somewhere the user never asked for, and real URLs can be far
    /// longer than any fixed ceiling. A URL that will not parse as an absolute
    /// navigable address is rejected instead, which fails loudly by leaving no
    /// record rather than quietly by opening the wrong tab later.
    /// </summary>
    public static ClosedTabRecord? Create(string? url, string? title, bool wasPinned, DateTime closedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)) return null;
        if (!ReopenableSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase)) return null;

        var label = title ?? "";
        if (label.Length > ClosedTabStore.MaxTitleLength)
            label = label[..ClosedTabStore.MaxTitleLength];

        return new ClosedTabRecord(trimmed, label, wasPinned, closedAtUtc);
    }

    /// <summary>True when the address can be navigated to as it stands.</summary>
    public bool CanReopen() =>
        Uri.TryCreate(Url, UriKind.Absolute, out var parsed) &&
        ReopenableSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase);
}