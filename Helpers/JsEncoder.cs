namespace SpurBrowser.Helpers;

/// <summary>
/// Encodes strings for safe embedding in JavaScript string literals
/// inside HTML attributes (onclick, etc.).
/// </summary>
public static class JsEncoder
{
    /// <summary>
    /// Escapes a string so it can be safely embedded inside a JS string literal
    /// that itself is inside an HTML attribute.
    /// </summary>
    public static string Encode(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "&quot;")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }
}
