using System;

namespace StrideBrowser.Engine.Handlers;

/// <summary>
/// Implemented by any message handler that can emit setting changes.
/// The router subscribes generically â€” no downcasting needed.
/// </summary>
public interface ISettingEmitter
{
    event Action<string, string>? SettingChanged;
}
