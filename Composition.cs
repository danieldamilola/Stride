using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;

namespace StrideBrowser;

/// <summary>
/// Composition root — registers all services into the DI container.
/// Called once at startup from App.xaml.cs.
/// </summary>
public static class Composition
{
    /// <summary>Builds and returns the configured service provider.</summary>
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Settings (load eagerly so other services can depend on it)
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();

        // ThemeManager is now a singleton
        services.AddSingleton<ThemeManager>();

        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton(settings);

        // Services — registered by interface and concrete type
        services.AddSingleton<NavigationService>();
        services.AddSingleton<FaviconLoader>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<HistoryStore>());
        services.AddSingleton<OneTabStore>();
        services.AddSingleton<IOneTabStore>(sp => sp.GetRequiredService<OneTabStore>());
        services.AddSingleton<DownloadStore>();
        services.AddSingleton<IDownloadStore>(sp => sp.GetRequiredService<DownloadStore>());
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<ExtensionManager>();
        services.AddSingleton(sp => new InternalPages(sp.GetRequiredService<ThemeManager>()));
        services.AddSingleton<YouTubeEnhancer>();
        services.AddSingleton<YouTubeUnhook>();
        services.AddSingleton<FocusBlocklistService>();
        services.AddSingleton<Engine.ContentScriptInjector>();
        services.AddSingleton<CustomDownloadManager>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<TabHibernationManager>();
        services.AddSingleton<NavigationPolicyEngine>();

        // Reader mode - scaffold: interfaces registered, real bodies in step 2
        services.AddSingleton<Services.Reader.IReaderSanitizer, Services.Reader.ReaderSanitizer>();
        services.AddSingleton<Services.Reader.IReaderExtractor, Services.Reader.ReaderExtractor>();
        services.AddSingleton<Services.Reader.IReaderTemplateRenderer, Services.Reader.ReaderTemplateRenderer>();
        services.AddSingleton<Services.Reader.IReaderService, Services.Reader.ReaderService>();

        // Engine dependencies record
        services.AddSingleton(sp => new EngineDependencies
        {
            ExtensionManager = sp.GetRequiredService<ExtensionManager>(),
            YouTubeUnhook = sp.GetRequiredService<YouTubeUnhook>(),
            Settings = sp.GetRequiredService<BrowserSettings>(),
            FaviconLoader = sp.GetRequiredService<FaviconLoader>(),
            Pages = sp.GetRequiredService<InternalPages>(),
            HistoryStore = sp.GetRequiredService<IHistoryStore>(),
            OneTabStore = sp.GetRequiredService<IOneTabStore>(),
            DownloadStore = sp.GetRequiredService<IDownloadStore>(),
            FocusBlocklistService = sp.GetRequiredService<FocusBlocklistService>(),
            ContentScriptInjector = sp.GetRequiredService<ContentScriptInjector>(),
            CustomDownloadManager = sp.GetRequiredService<CustomDownloadManager>(),
            HibernationManager = sp.GetRequiredService<TabHibernationManager>(),
            NavigationPolicyEngine = sp.GetRequiredService<NavigationPolicyEngine>(),
            ThemeManager = sp.GetRequiredService<ThemeManager>()
        });
        services.AddSingleton<TabEngine>();
        
        // Message Handlers
        services.AddSingleton<IWebMessageHandler, CoreMessageHandler>();
        services.AddSingleton<IWebMessageHandler, SettingsMessageHandler>();
        services.AddSingleton<IWebMessageHandler, OneTabMessageHandler>();
        services.AddSingleton<IWebMessageHandler, HistoryMessageHandler>();
        services.AddSingleton<IWebMessageHandler, ShortcutMessageHandler>();
        services.AddSingleton<IWebMessageHandler, DownloadMessageHandler>();
        services.AddSingleton<IWebMessageHandler, OnboardingMessageHandler>();
        services.AddSingleton<IWebMessageHandler, TCLensMessageHandler>();

        services.AddSingleton<WebMessageRouter>();
        services.AddSingleton<TCLensTransferService>();

        // ViewModel and Views
        services.AddSingleton<BrowserViewModel>();
        services.AddSingleton<ViewModels.Reader.ReaderViewModel>(sp =>
        {
            var readerService = sp.GetRequiredService<Services.Reader.IReaderService>();
            var engine = sp.GetRequiredService<TabEngine>();
            return new ViewModels.Reader.ReaderViewModel(readerService, () => engine.ActiveTab?.Id);
        });
        services.AddTransient<MainWindow>();

        var sp = services.BuildServiceProvider();
        
        // Eagerly resolve ThemeManager so it can apply the initial theme
        sp.GetRequiredService<ThemeManager>();

        // Wire ReaderService cleanup on tab close and single-WebView reader guard
        var tabEngine = sp.GetRequiredService<TabEngine>();
        var readerService = sp.GetRequiredService<Services.Reader.IReaderService>();
        tabEngine.TabClosed += readerService.RemoveSession;
        tabEngine.IsReaderActive = tabId => readerService.GetSession(tabId)?.IsInReader == true;
        tabEngine.ExitReaderAsync = tabId => readerService.ExitReaderAsync(tabId);
        
        return sp;
    }
}
