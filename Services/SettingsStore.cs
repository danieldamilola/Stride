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
            loaded.AccentColor = NormalizeAccentColor(loaded.AccentColor);
            return loaded;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"SettingsStore.Load failed: {ex.Message}");
            return new BrowserSettings();
        }
    }

    /// <summary>
    /// Forces AccentColor to a hex literal, falling back to the default otherwise.
    /// <para>
    /// AccentColor reaches the settings page as a raw CSS custom property and as a value
    /// inside a JavaScript string literal. The IPC setter checks the shape when the user
    /// changes it, but a hand-edited or tampered settings.json is deserialized straight into
    /// the model, so the check has to be repeated where the file is read.
    /// </para>
    /// </summary>
    internal static string NormalizeAccentColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultAccentColor;
        var trimmed = value.Trim();
        if (trimmed.Length is 4 or 7 && trimmed[0] == '#' && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            if (trimmed.Length == 4)
                return $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}";
            return trimmed;
        }
        return DefaultAccentColor;
    }

    private const string DefaultAccentColor = "#D4A574";

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
