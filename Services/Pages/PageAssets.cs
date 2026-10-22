using System.Diagnostics;
using System.IO;
using StrideBrowser.Helpers;

namespace StrideBrowser.Services.Pages;

internal static class PageAssets
{
    private static readonly Lazy<string> Icon = new(() =>
    {
        try
        {
            using var stream = ResourceLoader.OpenStream("icons.stride-browser-128x128.png");
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
        }
        catch (Exception ex) { Trace.WriteLine(ex); return ""; }
    });
    public static string AppIcon => Icon.Value;
}
