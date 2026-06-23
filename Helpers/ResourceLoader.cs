using System.IO;
using System.Reflection;

namespace SpurBrowser.Helpers;

/// <summary>
/// Loads embedded resource files and optionally applies placeholder substitution.
/// Resources are embedded via the .csproj EmbeddedResource directive.
/// </summary>
public static class ResourceLoader
{
    private static readonly Assembly Asm = Assembly.GetExecutingAssembly();
    private static readonly string Prefix = typeof(ResourceLoader).Namespace!.Split('.')[0]; // "SpurBrowser"

    /// <summary>
    /// Loads an embedded resource as a string.
    /// <paramref name="resourceName"/> should use dots as path separators,
    /// e.g. "Resources.Scripts.youtube-enhancer.js".
    /// </summary>
    public static string Load(string resourceName)
    {
        var fullName = $"{Prefix}.{resourceName}";
        using var stream = Asm.GetManifestResourceStream(fullName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Loads an embedded resource and replaces {{KEY}} placeholders
    /// with the corresponding values from <paramref name="replacements"/>.
    /// </summary>
    public static string LoadTemplate(string resourceName, Dictionary<string, string> replacements)
    {
        var template = Load(resourceName);
        foreach (var (key, value) in replacements)
            template = template.Replace($"{{{{{key}}}}}", value);
        return template;
    }

    /// <summary>
    /// Tries to load an embedded resource. Returns null if not found.
    /// </summary>
    public static string? TryLoad(string resourceName)
    {
        var fullName = $"{Prefix}.{resourceName}";
        using var stream = Asm.GetManifestResourceStream(fullName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
