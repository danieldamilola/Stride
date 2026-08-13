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

    public TCLensMessageHandler(TabEngine engine)
    {
        _engine = engine;
    }

    public void Register(Dictionary<string, Func<string, Task>> prefixHandlers, Dictionary<string, Func<Task>> exactHandlers)
    {
        prefixHandlers[WebMessagePrefix.TCLensGetText] = HandleTCLensGetText;
    }

    private async Task HandleTCLensGetText(string _)
    {
        var activeTab = _engine.ActiveTab;
        if (activeTab == null) return;
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;

        var payload = new Dictionary<string, string>
        {
            ["type"] = "tclens-text",
            ["text"] = MainWindow.PendingTCLensText ?? "",
            ["url"] = MainWindow.PendingTCLensUrl ?? "",
            ["title"] = MainWindow.PendingTCLensTitle ?? ""
        };
        
        var jsonPayload = JsonSerializer.Serialize(payload);
        await wv.ExecuteScriptAsync($"if (window.tclensProcessInjection) window.tclensProcessInjection({jsonPayload});");
    }
}
