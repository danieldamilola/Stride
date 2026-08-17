using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.MessageHandlers;

public class ShortcutMessageHandler : IWebMessageHandler, ISettingEmitter
{
    private readonly TabEngine _engine;
    private readonly BrowserViewModel _vm;
    private readonly ISettingsStore _settingsStore;

    public event Action<string, string>? SettingChanged;

    public ShortcutMessageHandler(TabEngine engine, BrowserViewModel vm, ISettingsStore settingsStore)
    {
        _engine = engine;
        _vm = vm;
        _settingsStore = settingsStore;
    }

    public IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers()
    {
        return new Dictionary<string, Func<string, Task>>
        {
            [WebMessagePrefix.ShortcutAdd] = HandleShortcutAdd,
            [WebMessagePrefix.ShortcutRemove] = HandleShortcutRemove,
            [WebMessagePrefix.ShortcutClick] = HandleShortcutClick
        };
    }

    public IReadOnlyDictionary<string, Func<Task>> GetExactHandlers() => new Dictionary<string, Func<Task>>();

    private Task HandleShortcutAdd(string payload)
    {
        try
        {
            var item = JsonSerializer.Deserialize<ShortcutItem>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (item is null || string.IsNullOrWhiteSpace(item.Url)) return Task.CompletedTask;

            _vm.Settings.NewTabShortcuts.Add(item);
            _settingsStore.Save(_vm.Settings);
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
        if (index < 0 || index >= _vm.Settings.NewTabShortcuts.Count) return Task.CompletedTask;

        _vm.Settings.NewTabShortcuts.RemoveAt(index);
        _settingsStore.Save(_vm.Settings);
        SettingChanged?.Invoke("shortcuts", "");
        return Task.CompletedTask;
    }

    private Task HandleShortcutClick(string url)
    {
        if (_engine.ActiveTab is not null)
        {
            _engine.ActiveTab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(_engine.ActiveTab, url);
        }
        return Task.CompletedTask;
    }
}
