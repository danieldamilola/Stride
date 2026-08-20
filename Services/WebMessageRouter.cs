using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StrideBrowser.Engine.Handlers;

namespace StrideBrowser.Services;

/// <summary>
/// Dispatches web messages from internal pages to typed handlers.
/// Exact routes win over prefix routes (exact-first semantics).
/// </summary>
public sealed class WebMessageRouter
{
    private readonly Dictionary<string, Func<string, Task>> _prefixHandlers = new();
    private readonly Dictionary<string, Func<string, Task>> _exactHandlers = new();

    /// <summary>Fires when settings change so the view can apply live effects.</summary>
    public event Action<string, string>? SettingChanged;

    /// <summary>Fires when a handler navigates the active tab so the view can sync the address bar.</summary>
    public event Action<string>? AddressChanged;

    public WebMessageRouter(IEnumerable<IWebMessageHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            foreach (var route in handler.GetRoutes())
            {
                if (route.IsExact)
                    _exactHandlers[route.Key] = route.Handler;
                else
                    _prefixHandlers[route.Key] = route.Handler;
            }

            if (handler is ISettingEmitter emitter)
                emitter.SettingChanged += (k, v) => SettingChanged?.Invoke(k, v);

            if (handler is IAddressEmitter addressEmitter)
                addressEmitter.AddressChanged += url => AddressChanged?.Invoke(url);
        }
    }

    public async Task RouteAsync(string message)
    {
        try
        {
            if (_exactHandlers.TryGetValue(message, out var exactHandler))
            {
                await exactHandler(message);
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