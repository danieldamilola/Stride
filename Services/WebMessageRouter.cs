using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StrideBrowser.Services.MessageHandlers;

namespace StrideBrowser.Services;

/// <summary>
/// Dispatches web messages from internal pages to typed handlers.
/// Replaces the monolithic if/else chain formerly in MainWindow.
/// </summary>
public sealed class WebMessageRouter
{
    private readonly Dictionary<string, Func<string, Task>> _prefixHandlers = new();
    private readonly Dictionary<string, Func<Task>> _exactHandlers = new();

    /// <summary>Fires when settings change so the view can apply live effects.</summary>
    public event Action<string, string>? SettingChanged;

    public WebMessageRouter(IEnumerable<IWebMessageHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            handler.Register(_prefixHandlers, _exactHandlers);
            
            if (handler is SettingsMessageHandler settingsHandler)
                settingsHandler.SettingChanged += (k, v) => SettingChanged?.Invoke(k, v);
            else if (handler is ShortcutMessageHandler shortcutHandler)
                shortcutHandler.SettingChanged += (k, v) => SettingChanged?.Invoke(k, v);
        }
    }

    public async Task RouteAsync(string message)
    {
        try
        {
            if (_exactHandlers.TryGetValue(message, out var exactHandler))
            {
                await exactHandler();
                return;
            }

            foreach (var (prefix, handler) in _prefixHandlers)
            {
                if (message.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await handler(message[prefix.Length..]);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WebMessageRouter.RouteAsync failed for '{message}': {ex.Message}");
        }
    }
}
