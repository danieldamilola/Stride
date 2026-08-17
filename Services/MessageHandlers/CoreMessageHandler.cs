using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.MessageHandlers;

public class CoreMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly BrowserViewModel _vm;
    private readonly UpdateService _updateService;

    public CoreMessageHandler(TabEngine engine, BrowserViewModel vm, UpdateService updateService)
    {
        _engine = engine;
        _vm = vm;
        _updateService = updateService;
    }

    public IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers()
    {
        return new Dictionary<string, Func<string, Task>>
        {
            [WebMessagePrefix.Open] = HandleOpen,
            [WebMessagePrefix.Search] = HandleSearch
        };
    }

    public IReadOnlyDictionary<string, Func<Task>> GetExactHandlers()
    {
        return new Dictionary<string, Func<Task>>
        {
            [WebMessagePrefix.SetDefaultBrowser] = HandleSetDefaultBrowser
        };
    }

    private async Task HandleOpen(string url)
    {
        var tab = _engine.CreateTab(url);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleSearch(string query)
    {
        var url = _vm.ResolveInput(query);
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(_engine.ActiveTab, url);
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
