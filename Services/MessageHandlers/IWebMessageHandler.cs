using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrideBrowser.Services.MessageHandlers;

public interface IWebMessageHandler
{
    IReadOnlyDictionary<string, Func<string, Task>> GetPrefixHandlers();
    IReadOnlyDictionary<string, Func<Task>> GetExactHandlers();
}
