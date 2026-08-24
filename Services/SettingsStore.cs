using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Helpers;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Persists <see cref="BrowserSettings"/> to disk as JSON.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly string FilePath = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BrowserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var freshSettings = new BrowserSettings();
                // Do not auto import Edge bookmarks without consent - start with empty shortcuts
                freshSettings.NewTabShortcuts = new();
                return freshSettings;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<BrowserSettings>(json, JsonOpts) ?? new BrowserSettings();
            // A hand-edited or older-format file can contain null collections, which
            // would NRE in the settings page, new tab page, and shortcut handlers.
            loaded.NewTabShortcuts ??= new();
            loaded.CustomShortcuts ??= new();
            return loaded;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SettingsStore.Load failed: {ex.Message}");
            return new BrowserSettings();
        }
    }

    public void Save(BrowserSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            AtomicFileWriter.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SettingsStore.Save failed: {ex.Message}");
        }
    }
}
