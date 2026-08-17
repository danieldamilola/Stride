using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;

namespace StrideBrowser.Services.MessageHandlers;

public class TCLensMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly TCLensTransferService _transfer;

    public TCLensMessageHandler(TabEngine engine, TCLensTransferService transfer)
    {
        _engine = engine;
        _transfer = transfer;
    }

    public IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers()
    {
        return new Dictionary<string, Func<string, Task>>
        {
            [WebMessagePrefix.TCLensGetText] = HandleTCLensGetText
        };
    }

    public IReadOnlyDictionary<string, Func<Task>> GetExactHandlers() => new Dictionary<string, Func<Task>>();

    private async Task HandleTCLensGetText(string _)
    {
        var activeTab = _engine.ActiveTab;
        if (activeTab == null) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;

        var payload = new Dictionary<string, string>
        {
            ["text"] = _transfer.PendingText ?? "",
            ["url"] = _transfer.PendingUrl ?? "",
            ["title"] = _transfer.PendingTitle ?? ""
        };
        
        var jsonPayload = JsonSerializer.Serialize(payload);
        await wv.ExecuteScriptAsync($"if (window.tclensProcessInjection) window.tclensProcessInjection({jsonPayload});");
    }
}
