using System.Text.Json;
using StrideBrowser.Models;

namespace StrideBrowser.Services.Pages;

public sealed class OnboardingPage
{
    public string Render(BrowserSettings settings, string accentColor, string accentRgb, string ipcToken)
    {
        var payload = new
        {
            theme = settings.AppTheme.ToString().ToLowerInvariant(),
            accent = settings.AccentColor,
            floatingBar = settings.UseFloatingCommandBar,
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
        var json = JsonSerializer.Serialize(payload);
        return TemplateRenderer.RenderJsonPage("Resources.Pages.Onboarding.html", "ONBOARDING_DATA", json, accentColor, accentRgb, ipcToken);
    }
}
