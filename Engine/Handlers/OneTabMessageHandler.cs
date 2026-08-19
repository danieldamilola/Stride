using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class OneTabMessageHandler : IWebMessageHandler
{
    private readonly TabEngine _engine;
    private readonly IOneTabStore _oneTabStore;

    public OneTabMessageHandler(TabEngine engine, IOneTabStore oneTabStore)
    {
        _engine = engine;
        _oneTabStore = oneTabStore;
    }

    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabRestore, HandleOneTabRestore);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabDelete, HandleOneTabDelete);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabRename, HandleOneTabRename);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabOpen, HandleOneTabOpen);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabDeleteTab, HandleOneTabDeleteTab);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabStar, HandleOneTabStar);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabReorderTab, HandleOneTabReorderTab);
        yield return MessageRoute.Prefix(WebMessagePrefix.OneTabReorderGroup, HandleOneTabReorderGroup);
    }

    private async Task HandleOneTabRestore(string groupId)
    {
        var groups = _oneTabStore.Load();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        BrowserTab? lastTab = null;
        foreach (var entry in group.Tabs)
            lastTab = _engine.CreateTab(entry.Url, blockDuplicates: false);

        _oneTabStore.RemoveGroup(groupId);

        if (lastTab is not null)
            await _engine.ActivateAsync(lastTab);

        RefreshOneTabPages();
    }

    private Task HandleOneTabDelete(string groupId)
    {
        _oneTabStore.RemoveGroup(groupId);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabRename(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var newName)) return Task.CompletedTask;
        var groups = _oneTabStore.Load();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group is not null)
        {
            group.Name = newName;
            _oneTabStore.Save(groups);
            RefreshOneTabPages();
        }
        return Task.CompletedTask;
    }

    private async Task HandleOneTabOpen(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return;

        _oneTabStore.RemoveTab(groupId, url);
        RefreshOneTabPages();

        var tab = _engine.CreateTab(url, blockDuplicates: false);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleOneTabDeleteTab(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return Task.CompletedTask;

        _oneTabStore.RemoveTab(groupId, url);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabStar(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var indexStr)) return Task.CompletedTask;
        if (!int.TryParse(indexStr, out var tabIndex)) return Task.CompletedTask;

        _oneTabStore.ToggleStar(groupId, tabIndex);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabReorderTab(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 3) return Task.CompletedTask;
        var groupId = parts[0];
        if (!int.TryParse(parts[1], out var oldIdx) || !int.TryParse(parts[2], out var newIdx))
            return Task.CompletedTask;

        _oneTabStore.ReorderTab(groupId, oldIdx, newIdx);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private Task HandleOneTabReorderGroup(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 2) return Task.CompletedTask;
        if (!int.TryParse(parts[0], out var oldIdx) || !int.TryParse(parts[1], out var newIdx))
            return Task.CompletedTask;

        _oneTabStore.ReorderGroup(oldIdx, newIdx);
        RefreshOneTabPages();
        return Task.CompletedTask;
    }

    private void RefreshOneTabPages()
    {
        var groups = _oneTabStore.Load();
        foreach (var t in _engine.Tabs)
        {
            if (t.Url == InternalUrls.OneTab)
                _engine.NavigateToOneTab(t, groups);
        }
    }

    private static bool TrySplitPayload(string payload, out string id, out string value)
    {
        var sep = payload.IndexOf(':');
        if (sep < 0) { id = value = ""; return false; }
        id = payload[..sep];
        value = payload[(sep + 1)..];
        return true;
    }
}
