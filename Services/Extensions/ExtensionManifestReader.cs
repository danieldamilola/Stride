using System.IO;
using System.Text.Json;

namespace StrideBrowser.Services.Extensions;

public sealed record ExtensionManifestInfo(
    string Description,
    string Version,
    string IconDataUri,
    string? PopupPath,
    string? OptionsPath,
    IReadOnlyList<string> Permissions);

/// <summary>Reads display metadata without loading extension code.</summary>
public static class ExtensionManifestReader
{
    private const int ManifestLimit = 512 * 1024;
    private const int LocaleLimit = 256 * 1024;
    private const int IconLimit = 1024 * 1024;

    public static ExtensionManifestInfo Read(string folder)
    {
        try
        {
            var root = Path.GetFullPath(folder);
            if (!Directory.Exists(root) || IsReparsePoint(root))
                throw new InvalidDataException("The extension folder is missing or is a linked folder.");

            var manifestPath = ResolveFile(root, "manifest.json")
                ?? throw new InvalidDataException("The extension folder must contain a local manifest.json file.");
            using var document = ParseFile(manifestPath, ManifestLimit);
            var manifest = document.RootElement;
            if (manifest.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The extension manifest must be a JSON object.");

            var description = Text(manifest, "description", 4096);
            description = LocalizeDescription(root, manifest, description);
            var actions = new[] { Object(manifest, "action"), Object(manifest, "browser_action"), Object(manifest, "page_action") };
            string? popup = null;
            foreach (var action in actions)
                popup ??= RelativeFile(root, Text(action, "default_popup", 2048));
            var options = RelativeFile(root, Text(Object(manifest, "options_ui"), "page", 2048))
                ?? RelativeFile(root, Text(manifest, "options_page", 2048));

            var permissions = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in new[] { "permissions", "host_permissions" })
            {
                if (!manifest.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var item in array.EnumerateArray())
                {
                    if (permissions.Count >= 128) break;
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString()!;
                    if (value.Length is > 0 and <= 512 && !value.Any(char.IsControl) && seen.Add(value))
                        permissions.Add(value);
                }
            }

            var icon = ReadIcon(root, Object(manifest, "icons"));
            foreach (var action in actions)
            {
                if (icon.Length != 0) break;
                if (action.ValueKind == JsonValueKind.Object && action.TryGetProperty("default_icon", out var candidate))
                    icon = candidate.ValueKind == JsonValueKind.String
                        ? ReadIconFile(root, candidate.GetString()!)
                        : ReadIcon(root, candidate);
            }
            return new ExtensionManifestInfo(description, Text(manifest, "version", 128), icon, popup, options, permissions.AsReadOnly());
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The extension manifest could not be read. Check that manifest.json contains valid JSON and is no larger than 512 KB.", ex);
        }
    }

    private static string LocalizeDescription(string root, JsonElement manifest, string description)
    {
        if (!description.StartsWith("__MSG_", StringComparison.Ordinal) || !description.EndsWith("__", StringComparison.Ordinal))
            return description;
        var key = description[6..^2];
        if (key.Length == 0) return "";
        var locale = Text(manifest, "default_locale", 64);
        foreach (var language in new[] { locale, "en" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (language.Length == 0 || language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '@'))
                continue;
            try
            {
                var file = ResolveFile(root, $"_locales/{language}/messages.json");
                if (file is null) continue;
                using var document = ParseFile(file, LocaleLimit);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                // Chromium message names are case insensitive.
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    var message = Text(property.Value, "message", 4096);
                    if (message.Length != 0) return message;
                }
            }
            catch (Exception ex) when (IsOptionalFailure(ex)) { }
        }
        return "";
    }

    private static string ReadIcon(string root, JsonElement icons)
    {
        if (icons.ValueKind != JsonValueKind.Object) return "";
        var candidates = icons.EnumerateObject().Take(64)
            .Where(p => int.TryParse(p.Name, out var size) && size is > 0 and <= 512 && p.Value.ValueKind == JsonValueKind.String)
            .OrderBy(p => int.Parse(p.Name) > 256)
            .ThenByDescending(p => int.Parse(p.Name));
        foreach (var candidate in candidates)
        {
            var icon = ReadIconFile(root, candidate.Value.GetString()!);
            if (icon.Length != 0) return icon;
        }
        return "";
    }

    private static string ReadIconFile(string root, string relative)
    {
        try
        {
            var path = ResolveFile(root, relative);
            if (path is null) return "";
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".ico")) return "";
            var data = ReadBytes(path, IconLimit);
            string? mime = extension switch
            {
                ".png" when data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
                ".jpg" or ".jpeg" when data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255 => "image/jpeg",
                ".ico" when data.Length >= 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0 && (data[4] != 0 || data[5] != 0) => "image/x-icon",
                _ => null
            };
            return mime is null ? "" : $"data:{mime};base64,{Convert.ToBase64String(data)}";
        }
        catch (Exception ex) when (IsOptionalFailure(ex)) { return ""; }
    }

    private static bool IsOptionalFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;

    private static JsonDocument ParseFile(string path, int limit)
    {
        var data = ReadBytes(path, limit);
        var start = data.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
        return JsonDocument.Parse(data.AsMemory(start), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static byte[] ReadBytes(string path, int limit)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limit) throw new InvalidDataException($"The extension file exceeds its {limit / 1024} KB size limit.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer, 0, Math.Min(buffer.Length, limit + 1 - (int)output.Length))) > 0)
        {
            output.Write(buffer, 0, count);
            if (output.Length > limit) throw new InvalidDataException("The extension file exceeds its size limit.");
        }
        return output.ToArray();
    }

    private static string? RelativeFile(string root, string relative) => ResolveFile(root, relative) is { } file
        ? Path.GetRelativePath(root, file).Replace('\\', '/') : null;

    private static string? ResolveFile(string root, string relative)
    {
        if (relative.Length is 0 or > 2048 || Path.IsPathRooted(relative) || relative.Any(c => char.IsControl(c) || c is ':' or '?' or '#' or '%' or '\"' or '<' or '>' or '|' or '*'))
            return null;
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            return null;
        var path = root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
            if (!Path.Exists(path) || IsReparsePoint(path)) return null;
        }
        return File.Exists(path) ? path : null;
    }

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static JsonElement Object(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string Text(JsonElement parent, string name, int limit) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= limit ? text : "";
}
