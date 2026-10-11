using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Services;

/// <summary>State shared by browser windows for the lifetime of the process.</summary>
public sealed class BrowserSession
{
    public string WebViewDataDirectory { get; init; } = Helpers.AppPaths.WebView2Dir;
    public string IpcToken { get; } = Guid.NewGuid().ToString("N");
    public Dictionary<string, CoreWebView2DownloadOperation> NativeDownloads { get; } = new();
}
