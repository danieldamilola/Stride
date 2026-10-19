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
    private static readonly string DefaultFilePath = AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    /// <summary>
    /// Replaces the durable write when set. Null, the default, writes for real.
    /// This exists so a test can make a write fail, or fail once and then succeed,
    /// without needing a full disk. The product never sets it.
    /// </summary>
    private readonly Action<string, string>? _write;

    public SettingsStore() : this(null) { }

    /// <summary>Test seam: pass an explicit path to keep a run off the real profile.</summary>
    public SettingsStore(string? filePath) : this(filePath, null) { }

    /// <summary>Full test seam, including a controllable writer fault.</summary>
    public SettingsStore(string? filePath, Action<string, string>? write)
    {
        _filePath = filePath ?? DefaultFilePath;
        _write = write;
    }

    private string FilePath => _filePath;

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

            // One-time repairs for files an older build wrote. The result is saved, so a
            // repair runs once and a later choice the user makes is not undone on the
            // next start.
            if (SettingsMigration.Apply(loaded, SettingsMigration.RecordsSchemaVersion(json))) TrySave(loaded);

            return loaded;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SettingsStore.Load failed: {ex.Message}");
            return new BrowserSettings();
        }
    }

    /// <summary>
    /// Writes the settings and reports whether the file now holds them. The answer
    /// is the write's own: a locked file, a full disk or a path that cannot be
    /// written returns false, and the previous settings file is left on disk
    /// because the atomic write only renames over the target once the new bytes are
    /// down. Never throws, because a window close cannot be taken down by a save
    /// that failed.
    /// </summary>
    public bool TrySave(BrowserSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            if (_write is not null) _write(FilePath, json);
            else AtomicFileWriter.WriteAllText(FilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SettingsStore save failed: {ex.Message}");
            return false;
        }
    }

    public void Save(BrowserSettings settings) => TrySave(settings);
}
