using System.Net.Http;

namespace UmberBrowser.Services.Downloads;

/// <summary>
/// Decisions for the inline-response fallback. Some download URLs answer with
/// content a browser renders instead of an attachment, so WebView2 never
/// raises DownloadStarting and the hidden transfer window adopts nothing.
/// The fallback then fetches the bytes with HTTP directly. These helpers keep
/// the policy decisions pure so they can be tested without a web stack.
/// </summary>
public static class DownloadFallbackPolicy
{
    /// <summary>
    /// Media types the hidden window renders as a document. Saving those
    /// would store an error page or script text as the user's file, so the
    /// fallback refuses them and the request keeps its original failure.
    /// </summary>
    private static readonly string[] RenderedTypes =
    {
        "text/html", "text/plain", "text/xml", "application/xhtml", "application/xml",
        "text/javascript", "application/javascript", "application/json"
    };

    /// <summary>True when the response body is worth saving as a file.</summary>
    public static bool ShouldAttempt(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return true;
        var type = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        foreach (var denied in RenderedTypes)
            if (type.StartsWith(denied, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// Plans the resume of a download that is already part written on disk.
    /// Appending needs the server to accept ranges and a validator to catch a
    /// changed file, so without both the download restarts from zero.
    /// </summary>
    public static (long Offset, string? IfRange) PlanResume(
        bool supportsRanges, long offset, string? eTag, string? lastModified)
    {
        if (offset <= 0 || !supportsRanges) return (0, null);
        return (eTag is not null || lastModified is not null)
            ? (offset, eTag ?? lastModified)
            : (0, null);
    }

    /// <summary>The message for a HTTP status that can never yield a file, or null.</summary>
    public static string? DescribeStatus(int status) => status switch
    {
        401 or 403 or 407 => "This download needs permission. Sign in on the website and download the file again.",
        404 or 410 => "The server could not provide the file. Try the download link on the website again.",
        >= 500 => "The server could not provide the file. Try the download link on the website again.",
        _ => null
    };

    /// <summary>The message for an exception while streaming the response body.</summary>
    public static string DescribeException(Exception ex) => ex switch
    {
        HttpRequestException => "The connection was lost. Check your internet connection and retry.",
        TaskCanceledException => "The connection timed out. Retry the download.",
        _ => "The download was interrupted. Retry, or use the download link on the website again."
    };
}
