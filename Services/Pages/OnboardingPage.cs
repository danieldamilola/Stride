using System.Text.Json;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

public sealed class OnboardingPage
{
    public string Render(BrowserSettings settings, string accentColor, string accentRgb, string ipcToken)
    {
        var html = TemplateRenderer.RenderJsonPage("Resources.Pages.Onboarding.html", "ONBOARDING_DATA", SerializeSettings(settings), accentColor, accentRgb, ipcToken);
        return html.Replace("{{APP_ICON}}", PageAssets.AppIcon);
    }

    public static string SerializeSettings(BrowserSettings settings)
    {
        var payload = new
        {
            theme = settings.AppTheme.ToString().ToLowerInvariant(),
            accent = settings.AccentColor,
            adBlock = settings.AdBlockEnabled,
            smartScreen = settings.SmartScreenEnabled,
            httpsForce = settings.ForceHttps,
            clearOnExit = settings.ClearDataOnExit,
            hibernate = settings.TabHibernationEnabled,
            tabSleep = settings.TabSleepEnabled,
            engine = settings.SearchEngine switch
            {
                "Brave" => "brave",
                "Startpage" => "start",
                "Google" => "google",
                "Bing" => "bing",
                _ => "ddg"
            },
            showTabNames = settings.ShowTabNames
        };
        return JsonSerializer.Serialize(payload);
    }
}
