using System;

namespace StrideBrowser.Engine.Handlers;

/// <summary>
/// Implemented by any message handler that navigates the active tab and wants
/// the address bar to follow along. The router forwards to its AddressChanged
/// event - handlers never touch the view model.
/// </summary>
public interface IAddressEmitter
{
    event Action<string>? AddressChanged;
}