using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
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
        UpdateOneTabPages(new { action = "removeGroup", groupId });

        if (lastTab is not null)
            await _engine.ActivateAsync(lastTab);

    }

    private Task HandleOneTabDelete(string groupId)
    {
        _oneTabStore.RemoveGroup(groupId);
        UpdateOneTabPages(new { action = "removeGroup", groupId });
        return Task.CompletedTask;
    }

    private Task HandleOneTabRename(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var newName)) return Task.CompletedTask;
        _oneTabStore.RenameGroup(groupId, newName);
        UpdateOneTabPages(new { action = "rename", groupId, name = newName });
        return Task.CompletedTask;
    }

    private async Task HandleOneTabOpen(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return;

        _oneTabStore.RemoveTab(groupId, url);
        UpdateOneTabPages(new { action = "removeTab", groupId, url });

        var tab = _engine.CreateTab(url, blockDuplicates: false);
        await _engine.ActivateAsync(tab);
    }

    private Task HandleOneTabDeleteTab(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var url)) return Task.CompletedTask;

        _oneTabStore.RemoveTab(groupId, url);
        UpdateOneTabPages(new { action = "removeTab", groupId, url });
        return Task.CompletedTask;
    }

    private Task HandleOneTabStar(string payload)
    {
        if (!TrySplitPayload(payload, out var groupId, out var indexStr)) return Task.CompletedTask;
        if (!int.TryParse(indexStr, out var tabIndex)) return Task.CompletedTask;

        _oneTabStore.ToggleStar(groupId, tabIndex);
        UpdateOneTabPages(new { action = "star", groupId, index = tabIndex });
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
        UpdateOneTabPages(new { action = "reorderTab", groupId, oldIndex = oldIdx, newIndex = newIdx });
        return Task.CompletedTask;
    }

    private Task HandleOneTabReorderGroup(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length < 2) return Task.CompletedTask;
        if (!int.TryParse(parts[0], out var oldIdx) || !int.TryParse(parts[1], out var newIdx))
            return Task.CompletedTask;

        _oneTabStore.ReorderGroup(oldIdx, newIdx);
        UpdateOneTabPages(new { action = "reorderGroup", oldIndex = oldIdx, newIndex = newIdx });
        return Task.CompletedTask;
    }

    private void UpdateOneTabPages(object update)
    {
        var message = _engine.IpcToken + ":onetab:" + JsonSerializer.Serialize(update);
        foreach (var t in _engine.Tabs)
        {
            if (t.Url != InternalUrls.OneTab) continue;
            try
            {
                if (_engine.GetWebViewElement(t.Id) is not { } view) continue;
                // Both WPF WebView2 control types expose their core on the owning dispatcher.
                dynamic webView = view;
                webView.CoreWebView2?.PostWebMessageAsString(message);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Saved tabs update failed: {ex}"); }
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
