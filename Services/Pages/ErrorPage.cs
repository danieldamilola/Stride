using System.Net;

namespace StrideBrowser.Services.Pages;

/// <summary>Generates the error page HTML for navigation failures.</summary>
public sealed class ErrorPage
{
    /// <summary>Returns the error page HTML with the failed URL and error message embedded.</summary>
    public string Render(string failedUrl, string errorMessage, string accentColor, string accentRgb)
    {
        return Helpers.ResourceLoader.LoadTemplate("Resources.Pages.Error.html",
            new Dictionary<string, string>
            {
                ["URL"] = WebUtility.HtmlEncode(failedUrl),
                ["ERROR_MESSAGE"] = WebUtility.HtmlEncode(errorMessage),
                ["RETRY_URL"] = WebUtility.HtmlEncode(failedUrl),
                ["ACCENT"] = accentColor,
                ["ACCENT_RGB"] = accentRgb
            });
    }
}
