using System;
using System.Threading.Tasks;
using UmberBrowser.Engine;
using UmberBrowser.Models;
using UmberBrowser.Services;

namespace UmberBrowser.Engine.Handlers;

public class OnboardingMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;

    public OnboardingMessageHandler(TabEngine engine, BrowserSettings settings, ISettingsStore settingsStore)
    {
        _engine = engine;
        _settings = settings;
        _settingsStore = settingsStore;
    }

    public System.Collections.Generic.IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Exact("onboarding-done", HandleDone);
        yield return MessageRoute.Exact("onboarding-ready", HandleReady);
        yield return MessageRoute.Exact("onboarding-reset", HandleReset);
        yield return MessageRoute.Exact(WebMessagePrefix.WhatsNewContinue, HandleWhatsNewContinue);
        yield return MessageRoute.Exact(WebMessagePrefix.WhatsNewDismiss, HandleWhatsNewDismiss);
    }

    private Task HandleReady()
    {
        // Push current settings snapshot so the page can hydrate if it missed the initial JSON block
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return Task.CompletedTask;

        var payload = Services.Pages.OnboardingPage.SerializeSettings(_settings);
        wv.PostWebMessageAsString(_engine.IpcToken + ":onboarding-data:" + payload);
        return Task.CompletedTask;
    }

    private Task HandleDone()
    {
        _settings.HasCompletedOnboarding = true;
        _settingsStore.Save(_settings);

        var active = _engine.ActiveTab;
        if (active != null && active.Url == InternalUrls.Onboarding)
        {
            _engine.CloseTab(active);
            if (_engine.Tabs.Count == 0)
            {
                var tab = _engine.CreateTab();
                _ = _engine.ActivateAsync(tab);
            }
            else
            {
                var next = _engine.Tabs[0];
                _engine.SwitchTo(next);
                _ = _engine.ActivateAsync(next);
            }
        }
        return Task.CompletedTask;
    }

    private Task HandleReset()
    {
        _settings.HasCompletedOnboarding = false;
        _settingsStore.Save(_settings);
        return Task.CompletedTask;
    }

    private Task HandleWhatsNewContinue()
    {
        var active = _engine.ActiveTab;
        if (active != null && active.Url == InternalUrls.ReleaseNotes)
        {
            if (_engine.Tabs.Count == 1)
            {
                _engine.Navigate(active, InternalUrls.NewTab);
                return Task.CompletedTask;
            }

            _engine.CloseTab(active);
            var next = _engine.Tabs[0];
            _engine.SwitchTo(next);
            _ = _engine.ActivateAsync(next);
        }
        return Task.CompletedTask;
    }

    private Task HandleWhatsNewDismiss()
    {
        var active = _engine.ActiveTab;
        if (active != null)
        {
            _engine.Navigate(active, InternalUrls.Settings);
        }
        return Task.CompletedTask;
    }
}
