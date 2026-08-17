using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrideBrowser.Engine.Handlers;

public interface IWebMessageHandler
{
    IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers();
    IReadOnlyDictionary<string, Func<Task>> GetExactHandlers();
}
