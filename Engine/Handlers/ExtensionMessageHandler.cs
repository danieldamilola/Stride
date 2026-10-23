using System.Text.Json;
using UmberBrowser.Models;
using UmberBrowser.Services.Extensions;
using UmberBrowser.Services.UI;

namespace UmberBrowser.Engine.Handlers;

public sealed class ExtensionMessageHandler(TabEngine engine, ExtensionLibrary library, ExtensionActions actions) : IWebMessageHandler, IDisposable
{
    private bool _syncing;
    private bool _disposed;
    private Action? _onChanged;
    public IEnumerable<MessageRoute> GetRoutes()
    {
        _onChanged ??= () => { if (!_disposed) _ = SyncAsync(); };
        library.Changed -= _onChanged;
        library.Changed += _onChanged;
        yield return MessageRoute.Exact("extensions-sync", SyncAsync);
        yield return MessageRoute.Exact("extensions-load", () => Run(actions.LoadAsync));
        yield return MessageRoute.Exact("extensions-reload-all", () => Run(actions.ReloadAllAsync));
        yield return MessageRoute.Prefix("extensions-developer:", value => Run(() => value is "true" or "false"
            ? library.SetDeveloperModeAsync(value == "true") : throw new InvalidOperationException("Invalid developer mode value.")));
        yield return MessageRoute.Prefix("extensions-toggle:", value => Run(async () =>
        {
            var payload = JsonSerializer.Deserialize<ToggleRequest>(value, ExtensionLibrary.JsonOptions) ?? throw new InvalidOperationException("Invalid extension request.");
            await actions.ToggleAsync(payload.Id, payload.Enabled);
        }));
        yield return MessageRoute.Prefix("extensions-pin:", value => Run(async () =>
        {
            var payload = JsonSerializer.Deserialize<PinRequest>(value, ExtensionLibrary.JsonOptions) ?? throw new InvalidOperationException("Invalid extension request.");
            await library.PinAsync(await actions.ReadyAsync(), payload.Id, payload.Pinned);
        }));
        yield return MessageRoute.Prefix("extensions-remove:", id => Run(async () => await library.RemoveAsync(await actions.ReadyAsync(), id)));
        yield return MessageRoute.Prefix("extensions-reload:", id => Run(async () => await library.ReloadAsync(await actions.ReadyAsync(), id)));
        yield return MessageRoute.Prefix("extensions-open-options:", id => Run(() => actions.OpenAsync(id, true)));
        yield return MessageRoute.Prefix("extensions-open-popup:", id => Run(() => actions.OpenAsync(id)));
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); Result(true, ""); }
        catch (Exception ex) { Result(false, ex.Message); }
        await SyncAsync();
    }
    private void Result(bool success, string message) => engine.PostMessageToExtensionsTabs("extensions-result:" + JsonSerializer.Serialize(new { success, message }));
    private async Task SyncAsync()
    {
        if (_disposed || _syncing || !engine.Tabs.Any(t => t.Url == InternalUrls.Extensions)) return;
        _syncing = true;
        try
        {
            var snapshot = await actions.SnapshotAsync();
            if (!_disposed) engine.PostMessageToExtensionsTabs("extensions-state:" + JsonSerializer.Serialize(snapshot, ExtensionLibrary.JsonOptions));
        }
        catch (Exception ex) { if (!_disposed) Result(false, ex.Message); }
        finally { _syncing = false; }
    }
    public void Dispose() { _disposed = true; if (_onChanged is not null) library.Changed -= _onChanged; }
    private sealed record ToggleRequest(string Id, bool Enabled);
    private sealed record PinRequest(string Id, bool Pinned);
}
