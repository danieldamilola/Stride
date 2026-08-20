using System.Collections.Generic;

namespace StrideBrowser.Engine.Handlers;

/// <summary>
/// Contract for web-message handlers. Routes are declared as a flat list of
/// <see cref="MessageRoute"/> records — no handler-side dictionaries leak out.
/// </summary>
public interface IWebMessageHandler
{
    IEnumerable<MessageRoute> GetRoutes();
}