using System.Diagnostics;
using System.IO;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services.Pages;

internal static class PageAssets
{
    private static readonly Lazy<string> Icon = new(() =>
    {
        try
        {
            using var stream = ResourceLoader.OpenStream(AppIconResources.AboutResource);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
        }
        catch (Exception ex) { Trace.WriteLine(ex); return ""; }
    });
    public static string AppIcon => Icon.Value;
}
