using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using StrideBrowser.Engine;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// UpdateService lives for the whole process, while the settings handler belongs to one
/// window scope. The handler forwards update events to settings tabs, so a subscription
/// that outlives the scope keeps a closed window's engine and handlers alive.
/// </summary>
public sealed class SettingsHandlerLifetimeTests
{
    [Fact]
    public void DisposingTheHandler_ReleasesBothUpdateSubscriptions()
    {
        using var http = new HttpClient();
        var update = new UpdateService(http);
        var handler = new SettingsMessageHandler(
            (TabEngine)RuntimeHelpers.GetUninitializedObject(typeof(TabEngine)),
            new BrowserSettings(),
            new NullSettingsStore(),
            update,
            (TabHibernationManager)RuntimeHelpers.GetUninitializedObject(typeof(TabHibernationManager)));

        Assert.Single(Subscribers(update, nameof(UpdateService.UpdateFailed)));
        Assert.Single(Subscribers(update, nameof(UpdateService.DownloadProgressChanged)));

        handler.Dispose();
        handler.Dispose();

        Assert.Empty(Subscribers(update, nameof(UpdateService.UpdateFailed)));
        Assert.Empty(Subscribers(update, nameof(UpdateService.DownloadProgressChanged)));
    }

    private static Delegate[] Subscribers(object source, string eventName)
    {
        var field = source.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (field!.GetValue(source) as Delegate)?.GetInvocationList() ?? [];
    }

    private sealed class NullSettingsStore : ISettingsStore
    {
        public BrowserSettings Load() => new();
        public bool TrySave(BrowserSettings settings) => true;
        public void Save(BrowserSettings settings) { }
    }
}
