using System;

namespace StrideBrowser.Services.MessageHandlers;

/// <summary>
/// Implemented by any message handler that can emit setting changes.
/// The router subscribes generically — no downcasting needed.
/// </summary>
public interface ISettingEmitter
{
    event Action<string, string>? SettingChanged;
}
