namespace StrideBrowser.Services;

/// <summary>Interface for the recently-closed tab persistence layer.</summary>
public interface IClosedTabStore
{
    List<ClosedTabEntry> Load();
    void Save(IEnumerable<ClosedTabEntry> tabs);
}

/// <summary>A tab that was closed and can be reopened.</summary>
public record ClosedTabEntry(string Url, string Title);