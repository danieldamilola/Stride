namespace SpurBrowser.Models;

/// <summary>A single browsing history record.</summary>
public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime VisitedAt { get; set; } = DateTime.UtcNow;
}
