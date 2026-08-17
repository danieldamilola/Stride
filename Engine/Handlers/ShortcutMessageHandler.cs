using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class ShortcutMessageHandler : IWebMessageHandler, ISettingEmitter, IAddressEmitter
{
    private readonly TabEngine _engine;
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;

    public event Action<string, string>? SettingChanged;
    public event Action<string>? AddressChanged;

    public ShortcutMessageHandler(TabEngine engine, BrowserSettings settings, ISettingsStore settingsStore)
    {
        _engine = engine;
        _settings = settings;
        _settingsStore = settingsStore;
    }

    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.ShortcutAdd, HandleShortcutAdd);
        yield return MessageRoute.Prefix(WebMessagePrefix.ShortcutRemove, HandleShortcutRemove);
        yield return MessageRoute.Prefix(WebMessagePrefix.ShortcutClick, HandleShortcutClick);
    }

    private Task HandleShortcutAdd(string payload)
    {
        try
        {
            var item = JsonSerializer.Deserialize<ShortcutItem>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (item is null || string.IsNullOrWhiteSpace(item.Url)) return Task.CompletedTask;

            _settings.NewTabShortcuts.Add(item);
            _settingsStore.Save(_settings);
            SettingChanged?.Invoke("shortcuts", "");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"HandleShortcutAdd failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private Task HandleShortcutRemove(string payload)
    {
        if (!int.TryParse(payload, out var index)) return Task.CompletedTask;
        if (index < 0 || index >= _settings.NewTabShortcuts.Count) return Task.CompletedTask;

        _settings.NewTabShortcuts.RemoveAt(index);
        _settingsStore.Save(_settings);
        SettingChanged?.Invoke("shortcuts", "");
        return Task.CompletedTask;
    }

    private Task HandleShortcutClick(string url)
    {
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _engine.Navigate(_engine.ActiveTab, url);
            AddressChanged?.Invoke(url);
        }
        return Task.CompletedTask;
    }
}