using SpurBrowser.Models;

namespace SpurBrowser.Services;

/// <summary>Interface for the history persistence layer.</summary>
public interface IHistoryStore
{
    void Add(string url, string title);
    List<HistoryEntry> Load();
    List<HistoryEntry> Search(string query);
    void Clear();
    List<HistoryEntry> GetFrequentSites(int count = 8);
}
