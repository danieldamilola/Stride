using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>Interface for the history persistence layer.</summary>
public interface IHistoryStore
{
    void Add(string url, string title);
    List<HistoryEntry> Load();
    List<HistoryEntry> Search(string query);
    bool Clear();
    List<HistoryEntry> GetFrequentSites(int count = 8);
}
