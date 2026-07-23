namespace StrideBrowser.Abstractions;

public interface IFaviconLoader
{
    Task<byte[]?> LoadAsync(string url);
    Task<byte[]?> HandleFaviconChangedAsync(IWebViewEngine engine, Guid tabId, string url);
}
