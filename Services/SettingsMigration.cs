using UmberBrowser.Models;

namespace UmberBrowser.Services;

/// <summary>
/// One-time repairs applied when a settings.json written by an older build is read.
///
/// Each repair is keyed to a schema version, so it runs once and then stops. That is the
/// whole point: a repair that ran on every start would also run after the user had made
/// their own choice, and would overwrite it.
/// </summary>
public static class SettingsMigration
{
    /// <summary>
    /// Brings a loaded settings object up to <see cref="BrowserSettings.CurrentSchemaVersion"/>
    /// and reports whether anything was changed.
    ///
    /// <paramref name="schemaVersionWasRecorded"/> says whether the file carried a
    /// <c>settingsSchemaVersion</c> key at all. A file written before the key existed
    /// cannot be read through the property default, because that default is the current
    /// version, so the key's presence is what distinguishes an old file from a new one.
    ///
    /// Version 1: the third-party favicon fallback becomes opt-in. Earlier builds
    /// defaulted <see cref="BrowserSettings.FaviconProxyEnabled"/> to true, so a profile
    /// written by one of them carries a saved true that says nothing about what the user
    /// wanted. It is switched off once here. The version is then written back, so the
    /// repair does not run again and a later choice the user makes is left alone.
    /// Version 2: online search suggestions become opt-in for the same reason.
    /// </summary>
    public static bool Apply(BrowserSettings settings, bool schemaVersionWasRecorded)
    {
        if (settings is null) return false;
        if (schemaVersionWasRecorded && settings.SettingsSchemaVersion >= BrowserSettings.CurrentSchemaVersion)
            return false;

        var previousVersion = schemaVersionWasRecorded ? settings.SettingsSchemaVersion : 0;
        if (previousVersion < BrowserSettings.FaviconConsentSchemaVersion)
            settings.FaviconProxyEnabled = false;
        if (previousVersion < BrowserSettings.SearchSuggestionsConsentSchemaVersion)
            settings.SearchSuggestionsEnabled = false;
        settings.SettingsSchemaVersion = BrowserSettings.CurrentSchemaVersion;
        return true;
    }

    /// <summary>
    /// True when the file carries a settingsSchemaVersion key, whatever its value. A file
    /// that does not is one an older build wrote, and a file that cannot be read at all is
    /// treated the same way, because there is nothing else to go on.
    /// </summary>
    public static bool RecordsSchemaVersion(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;

            foreach (var property in document.RootElement.EnumerateObject())
                if (property.NameEquals("settingsSchemaVersion") ||
                    string.Equals(property.Name, "settingsSchemaVersion", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (System.Text.Json.JsonException) { /* an unreadable file is an old file */ }

        return false;
    }
}
