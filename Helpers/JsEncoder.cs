namespace StrideBrowser.Helpers;

/// <summary>
/// Encodes strings for safe embedding in JavaScript string literals
/// inside HTML attributes (onclick, etc.).
/// </summary>
public static class JsEncoder
{
    /// <summary>
    /// Escapes a string so it can be safely embedded inside a JS string literal
    /// that itself is inside an HTML attribute. Use this for values that land in
    /// double-quoted attributes (data-action, onclick, title) and the JS literal
    /// inside them. For plain HTML text content, use <see cref="HtmlEncode"/>.
    /// </summary>
    public static string Encode(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "&quot;")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    /// <summary>
    /// Escapes a string for safe embedding as HTML text content. The browser
    /// will parse the result as text rather than markup, so angle brackets
    /// and ampersands must be neutralized.
    /// </summary>
    public static string HtmlEncode(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
