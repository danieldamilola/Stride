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

    public void Register(Dictionary<string, Func<string, Task>> prefixHandlers, Dictionary<string, Func<Task>> exactHandlers)
    {
        prefixHandlers[WebMessagePrefix.Open] = HandleOpen;
        prefixHandlers[WebMessagePrefix.Search] = HandleSearch;
        exactHandlers[WebMessagePrefix.SetDefaultBrowser] = HandleSetDefaultBrowser;
        exactHandlers[WebMessagePrefix.CheckForUpdate] = HandleCheckForUpdate;
        exactHandlers[WebMessagePrefix.InstallUpdate] = HandleInstallUpdate;
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

    private async Task HandleCheckForUpdate()
    {
        var hasUpdate = await _updateService.CheckForUpdatesAsync();
        var wv = _engine.GetCoreWebView2();
        if (wv != null)
        {
            if (hasUpdate)
            {
                var version = _updateService.LatestVersion ?? "Unknown";
                await wv.ExecuteScriptAsync($"if (typeof window.onUpdateCheckResult === 'function') window.onUpdateCheckResult(true, '{version}');");
            }
            else
            {
                await wv.ExecuteScriptAsync("if (typeof window.onUpdateCheckResult === 'function') window.onUpdateCheckResult(false, null);");
            }
        }
    }

    private async Task HandleInstallUpdate()
    {
        await _updateService.DownloadAndInstallUpdateAsync();
    }
}
