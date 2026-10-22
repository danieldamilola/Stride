using System.IO;
using System.Text.Json;

namespace StrideBrowser.Services.Extensions;

public sealed record ExtensionProfileEntry(string Id, string Folder, bool IsComponent);

/// <summary>Reads installed source paths that the native extension API does not expose.</summary>
public static class ExtensionProfileReader
{
    private const int PreferencesLimit = 16 * 1024 * 1024;

    public static IReadOnlyList<ExtensionProfileEntry> Read(string profilePath)
    {
        try
        {
            var profile = LocalFolder(profilePath);
            if (profile is null) return [];
            var file = Path.Combine(profile, "Secure Preferences");
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length > PreferencesLimit) return [];
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = input.Read(buffer, 0, Math.Min(buffer.Length, PreferencesLimit + 1 - (int)output.Length))) > 0)
            {
                output.Write(buffer, 0, count);
                if (output.Length > PreferencesLimit) return [];
            }
            var bytes = output.GetBuffer().AsMemory(0, (int)output.Length);
            if (bytes.Span.StartsWith(new byte[] { 239, 187, 191 })) bytes = bytes[3..];
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            if (!TryObject(document.RootElement, "extensions", out var extensions) || !TryObject(extensions, "settings", out var settings))
                return [];

            var entries = new List<ExtensionProfileEntry>();
            foreach (var setting in settings.EnumerateObject())
            {
                if (setting.Name.Length != 32 || setting.Name.Any(c => c is < 'a' or > 'p') || setting.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!setting.Value.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Number || !location.TryGetInt32(out var locationNumber) || locationNumber < 0)
                    continue;
                if (!setting.Value.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
                    continue;
                var folder = LocalFolder(path.GetString());
                if (folder is not null)
                    entries.Add(new(setting.Name, folder, locationNumber == 5));
            }
            return entries.AsReadOnly();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            // A browser may replace this file while an extension is being installed.
            return [];
        }
    }

    private static bool TryObject(JsonElement parent, string key, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static string? LocalFolder(string? path)
    {
        if (path is null || path.Length is < 4 or > 32767 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/'))
            return null;
        if (path.Any(c => char.IsControl(c) || c is '\"' or '<' or '>' or '|' or '?' or '*') || path.AsSpan(2).Contains(':'))
            return null;
        try
        {
            var folder = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(folder) || Path.TrimEndingDirectorySeparator(folder).Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder)!), StringComparison.OrdinalIgnoreCase))
                return null;
            return Directory.Exists(folder) ? Path.TrimEndingDirectorySeparator(folder) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
