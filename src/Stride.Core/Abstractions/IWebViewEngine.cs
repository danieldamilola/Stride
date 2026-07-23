namespace StrideBrowser.Abstractions;

public enum WebViewPreferredColorScheme { Auto, Dark, Light }

public enum WebViewScrollbarStyle { Default, FluentOverlay }

public record WebViewEnvironmentOptions(
    bool AreBrowserExtensionsEnabled,
    WebViewScrollbarStyle ScrollBarStyle,
    string AdditionalBrowserArguments,
    string DataDirectory);

public interface IWebViewEngine
{
    event Action<Guid, string>? NavigationStarting;
    event Action<Guid, bool, string?>? NavigationCompleted;
    event Action<Guid, string>? DocumentTitleChanged;
    event Action<Guid, string>? SourceChanged;
    event Action<Guid, byte[]?>? FaviconChanged;
    event Action<Guid, string>? WebMessageReceived;
    event Action<Guid, string, string>? DownloadStarting;
    event Action<Guid>? NewWindowRequested;
    event Action<Guid>? WindowCloseRequested;

    Task InitializeAsync(WebViewEnvironmentOptions options);
    Task<Guid> CreateWebViewAsync(string? initialUrl = null);
    Task NavigateAsync(Guid id, string url);
    Task NavigateToStringAsync(Guid id, string html);
    Task ExecuteScriptAsync(Guid id, string script);
    Task SetDarkModeAsync(Guid id, bool enabled);
    void ShowWebView(Guid id);
    void HideWebView(Guid id);
    void SuspendWebView(Guid id);
    Task ResumeWebViewAsync(Guid id);
    void SetZoomLevel(Guid id, double zoomFactor);
    double GetZoomLevel(Guid id);
    void DisposeWebView(Guid id);
    Task<byte[]?> GetFaviconAsync(Guid id);
    void SetBackgroundColor(Guid id, byte r, byte g, byte b);
    Task AddScriptToExecuteOnDocumentCreatedAsync(Guid id, string script);
    void AddWebResourceRequestedFilter(Guid id, string pattern);
    void Shutdown();
}
