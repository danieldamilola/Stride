using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrideBrowser.Services.MessageHandlers;

public interface IWebMessageHandler
{
    void Register(Dictionary<string, Func<string, Task>> prefixHandlers, Dictionary<string, Func<Task>> exactHandlers);
}
