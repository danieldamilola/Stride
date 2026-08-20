using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class HistoryMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly IHistoryStore _historyStore;

    public HistoryMessageHandler(TabEngine engine, IHistoryStore historyStore)
    {
        _engine = engine;
        _historyStore = historyStore;
    }

    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.HistoryOpen, HandleOpen);
        yield return MessageRoute.Exact(WebMessagePrefix.HistoryClear, HandleHistoryClear);
    }

    private async Task HandleOpen(string url)
    {
        var tab = _engine.CreateTab(url);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleHistoryClear()
    {
        _historyStore.Clear();
        foreach (var t in _engine.Tabs)
        {
            if (t.Url == InternalUrls.History)
                _engine.NavigateToHistory(t, []);
        }
        return Task.CompletedTask;
    }
}
