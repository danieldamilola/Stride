using System;
using System.Threading.Tasks;

namespace StrideBrowser.Engine.Handlers;

/// <summary>
/// A single web-message route. Exact routes are matched before prefix routes
/// (exact-first semantics preserved from the router).
/// </summary>
public sealed record MessageRoute(string Key, bool IsExact, Func<string, Task> Handler)
{
    public static MessageRoute Exact(string key, Func<Task> handler) => new(key, true, _ => handler());

    public static MessageRoute Prefix(string prefix, Func<string, Task> handler) => new(prefix, false, handler);
}