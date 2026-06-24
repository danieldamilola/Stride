using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>Interface for the OneTab persistence layer.</summary>
public interface IOneTabStore
{
    List<OneTabGroup> Load();
    void Save(List<OneTabGroup> groups);
    void AddGroup(OneTabGroup group);
    void RemoveGroup(string groupId);
    void RemoveTab(string groupId, string url);
    void ToggleStar(string groupId, int tabIndex);
    void ReorderGroup(int oldIndex, int newIndex);
    void ReorderTab(string groupId, int oldIndex, int newIndex);
}
