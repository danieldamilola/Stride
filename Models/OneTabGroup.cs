namespace SpurBrowser.Models;

/// <summary>
/// A named group of tabs saved via OneTab.
/// </summary>
public sealed class OneTabGroup
{
    /// <summary>Unique group identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User-visible group name.</summary>
    public string Name { get; set; } = "";

    /// <summary>UTC timestamp when the group was saved.</summary>
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tabs belonging to this group.</summary>
    public List<OneTabEntry> Tabs { get; set; } = [];
}

/// <summary>
/// A single tab entry within a <see cref="OneTabGroup"/>.
/// </summary>
public sealed class OneTabEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? FaviconUrl { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public bool IsStarred { get; set; }

    public OneTabEntry() { }

    public OneTabEntry(string url, string title, string? faviconUrl, DateTime savedAt)
    {
        Url = url;
        Title = title;
        FaviconUrl = faviconUrl;
        SavedAt = savedAt;
    }
}
