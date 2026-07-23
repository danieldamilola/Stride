using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>Interface for persisting browser settings.</summary>
public interface ISettingsStore
{
    BrowserSettings Load();
    void Save(BrowserSettings settings);
}
