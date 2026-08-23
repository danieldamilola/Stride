using System;
using System.IO;

namespace StrideBrowser.Services.CommandLine;

public interface ICommandLineUrlParser
{
    bool TryParse(string arg, out string url);
}

public class CommandLineUrlParser : ICommandLineUrlParser
{
    public bool TryParse(string arg, out string url)
    {
        if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("stride://", StringComparison.OrdinalIgnoreCase))
        {
            url = arg;
            return true;
        }

        try
        {
            if (File.Exists(arg))
            {
                url = new Uri(Path.GetFullPath(arg)).AbsoluteUri;
                return true;
            }
        }
        catch { }

        url = string.Empty;
        return false;
    }
}
