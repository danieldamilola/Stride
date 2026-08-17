using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class CoreMessageHandler : IWebMessageHandler, IAddressEmitter
{
    private readonly TabEngine _engine;
    private readonly NavigationService _navigation;

    public event Action<string>? AddressChanged;

    public CoreMessageHandler(TabEngine engine, NavigationService navigation)
    {
        _engine = engine;
        _navigation = navigation;
    }

    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.Open, HandleOpen);
        yield return MessageRoute.Prefix(WebMessagePrefix.Search, HandleSearch);
        yield return MessageRoute.Exact(WebMessagePrefix.SetDefaultBrowser, HandleSetDefaultBrowser);
    }

    private async Task HandleOpen(string url)
    {
        var tab = _engine.CreateTab(url);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleSearch(string query)
    {
        var url = _navigation.Resolve(query);
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _engine.Navigate(_engine.ActiveTab, url);
            AddressChanged?.Invoke(url);
        }
        return Task.CompletedTask;
    }

    private Task HandleSetDefaultBrowser()
    {
        DefaultBrowserRegistrar.Register();
        DefaultBrowserRegistrar.OpenDefaultAppsSettings();
        return Task.CompletedTask;
    }
}