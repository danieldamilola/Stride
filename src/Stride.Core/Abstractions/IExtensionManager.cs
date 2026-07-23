namespace StrideBrowser.Abstractions;

public interface IExtensionManager
{
    Task InitializeAsync(IWebViewEngine engine, Guid tabId);
    Task<string?> EnsureUBlockDownloadedAsync();
}
