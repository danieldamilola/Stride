using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SpurBrowser.Helpers;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

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
                return new BrowserSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<BrowserSettings>(json, JsonOpts) ?? new BrowserSettings();
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
