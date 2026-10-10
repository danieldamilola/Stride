namespace StrideBrowser.Services;

/// <summary>Session entry for saving/restoring tabs across restarts.</summary>
public record SessionEntry(string Url, string Title, bool IsPinned, bool IsActive = false);

/// <summary>Interface for the session persistence layer.</summary>
public interface ISessionStore
{
    List<SessionEntry> Load();
    void Save(IEnumerable<(string Url, string Title, bool IsPinned, bool IsActive)> tabs);
}
