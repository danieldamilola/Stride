using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>Interface for persisting browser settings.</summary>
public interface ISettingsStore
{
    BrowserSettings Load();

    /// <summary>
    /// Writes the settings and returns whether the file now holds them. The answer
    /// comes from the write itself, so a disk fault, a locked file or an unwritable
    /// path reports false rather than looking like a save.
    /// </summary>
    bool TrySave(BrowserSettings settings);

    /// <summary>Writes the settings. A write that fails is logged and never throws.</summary>
    void Save(BrowserSettings settings);
}
