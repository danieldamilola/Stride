using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

public class WebMessageRouterTests
{
    private sealed class FakeHandler : IWebMessageHandler
    {
        private readonly MessageRoute[] _routes;
        public FakeHandler(params MessageRoute[] routes) => _routes = routes;

        public IEnumerable<MessageRoute> GetRoutes() => _routes;
    }

    private sealed class EmittingHandler : IWebMessageHandler, ISettingEmitter, IAddressEmitter
    {
        public event Action<string, string>? SettingChanged;
        public event Action<string>? AddressChanged;

        public IEnumerable<MessageRoute> GetRoutes() => Array.Empty<MessageRoute>();

        public void EmitSetting(string key, string value) => SettingChanged?.Invoke(key, value);
        public void EmitAddress(string url) => AddressChanged?.Invoke(url);
    }

    private sealed class ThrowingHandler : IWebMessageHandler
    {
        public IEnumerable<MessageRoute> GetRoutes() =>
            new[] { MessageRoute.Prefix("boom:", _ => throw new InvalidOperationException("kaboom")) };
    }

    [Fact]
    public async Task ExactRoute_WinsOverPrefixRoute()
    {
        var prefixLog = new List<string>();
        var exactLog = new List<string>();
        var router = new WebMessageRouter(new IWebMessageHandler[]
        {
            new FakeHandler(MessageRoute.Prefix("tab:", h => Record(prefixLog, "prefix:" + h))),
            new FakeHandler(MessageRoute.Exact("tab:close", () => Record(exactLog, "exact")))
        });

        await router.RouteAsync("tab:close");

        Assert.Equal(new[] { "exact" }, exactLog);
        Assert.Empty(prefixLog);
    }

    [Fact]
    public async Task PrefixRoute_ReceivesPayloadSlicedAfterPrefix()
    {
        var log = new List<string>();
        var router = new WebMessageRouter(new IWebMessageHandler[]
        {
            new FakeHandler(MessageRoute.Prefix("search:", h => Record(log, h)))
        });

        await router.RouteAsync("search:hello world");

        Assert.Equal(new[] { "hello world" }, log);
    }

    [Fact]
    public async Task RoutesFromMultipleHandlers_AreMerged()
    {
        var aLog = new List<string>();
        var bLog = new List<string>();
        var router = new WebMessageRouter(new IWebMessageHandler[]
        {
            new FakeHandler(MessageRoute.Prefix("a:", h => Record(aLog, "a:" + h))),
            new FakeHandler(MessageRoute.Prefix("b:", h => Record(bLog, "b:" + h)))
        });

        await router.RouteAsync("b:2");
        await router.RouteAsync("a:1");

        Assert.Equal(new[] { "a:1" }, aLog);
        Assert.Equal(new[] { "b:2" }, bLog);
    }

    [Fact]
    public async Task UnknownMessage_IsIgnored()
    {
        var log = new List<string>();
        var router = new WebMessageRouter(new IWebMessageHandler[]
        {
            new FakeHandler(MessageRoute.Prefix("known:", h => Record(log, h)))
        });

        await router.RouteAsync("nope:not-registered");

        Assert.Empty(log);
    }

    [Fact]
    public async Task HandlerException_IsSwallowed()
    {
        var router = new WebMessageRouter(new IWebMessageHandler[] { new ThrowingHandler() });

        await router.RouteAsync("boom:now");

        Assert.True(true, "RouteAsync must not throw on handler failure");
    }

    [Fact]
    public void SettingChanged_IsForwardedFromEmitterHandlers()
    {
        var emitter = new EmittingHandler();
        var router = new WebMessageRouter(new IWebMessageHandler[] { emitter });
        (string key, string value)? seen = null;
        router.SettingChanged += (k, v) => seen = (k, v);

        emitter.EmitSetting("appTheme", "dark");

        Assert.Equal(("appTheme", "dark"), seen);
    }

    [Fact]
    public void AddressChanged_IsForwardedFromEmitterHandlers()
    {
        var emitter = new EmittingHandler();
        var router = new WebMessageRouter(new IWebMessageHandler[] { emitter });
        string? seen = null;
        router.AddressChanged += url => seen = url;

        emitter.EmitAddress("https://example.com");

        Assert.Equal("https://example.com", seen);
    }

    private static Task Record(List<string> log, string payload)
    {
        log.Add(payload);
        return Task.CompletedTask;
    }
}