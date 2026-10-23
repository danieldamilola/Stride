using System.Collections.Generic;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class SessionStoreTests
{
    [Fact]
    public void ResolveActiveIndex_EmptySession_ReturnsZero()
    {
        Assert.Equal(0, SessionStore.ResolveActiveIndex(new List<SessionEntry>()));
    }

    [Fact]
    public void ResolveActiveIndex_MarkedTab_ReturnsItsIndex()
    {
        var entries = new List<SessionEntry>
        {
            new("https://a.com", "A", false),
            new("https://b.com", "B", false, true),
            new("https://c.com", "C", true),
        };

        Assert.Equal(1, SessionStore.ResolveActiveIndex(entries));
    }

    [Fact]
    public void ResolveActiveIndex_NoneMarked_FallsBackToFirst()
    {
        var entries = new List<SessionEntry>
        {
            new("https://a.com", "A", false),
            new("https://b.com", "B", false),
        };

        Assert.Equal(0, SessionStore.ResolveActiveIndex(entries));
    }

    [Fact]
    public void ResolveActiveIndex_LegacyEntriesWithoutFlag_FallsBackToFirst()
    {
        var entries = new List<SessionEntry>
        {
            new("https://a.com", "A", true),
        };

        Assert.Equal(0, SessionStore.ResolveActiveIndex(entries));
    }
}
