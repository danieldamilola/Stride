using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
using StrideBrowser.Engine.Handlers;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;
using System.Net.Http;

namespace StrideBrowser;

/// <summary>
/// Composition root - registers all services into the DI container.
/// Called once at startup from App.xaml.cs.
/// </summary>
public static class Composition
{
    /// <summary>Builds and returns the configured service provider.</summary>
    /// <param name="configure">Extra registrations, used by a test host to substitute a store.</param>
    public static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // Settings (load eagerly so other services can depend on it)
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();

        // ThemeManager is now a singleton
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ISuggestionProvider, SuggestionProvider>();

        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton(settings);

        // Services - registered by interface and concrete type
        services.AddSingleton<NavigationService>();
        services.AddSingleton<FaviconLoader>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<HistoryStore>());
        services.AddSingleton<OneTabStore>();
        services.AddSingleton<IOneTabStore>(sp => sp.GetRequiredService<OneTabStore>());
        services.AddSingleton<DownloadStore>();
        services.AddSingleton<IDownloadStore>(sp => sp.GetRequiredService<DownloadStore>());
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<IClosedTabStore, ClosedTabStore>();
        services.AddSingleton<ExtensionManager>();
        services.AddSingleton(sp => new InternalPages(sp.GetRequiredService<ThemeManager>()));
        services.AddSingleton<YouTubeEnhancer>();
        services.AddSingleton<YouTubeUnhook>();
        services.AddSingleton<FocusBlocklistService>();
        services.AddSingleton<Engine.ContentScriptInjector>();
        services.AddSingleton<CustomDownloadManager>();
        services.AddSingleton<UpdateService>();
        // Scoped: the hibernation manager caches per tab suspensions for one engine.
        services.AddScoped<TabHibernationManager>();
        services.AddSingleton<NavigationPolicyEngine>();
        services.AddSingleton<Services.CommandLine.ICommandLineUrlParser, Services.CommandLine.CommandLineUrlParser>();
        services.AddSingleton<Services.Startup.IShellIntegrationService, Services.Startup.ShellIntegrationService>();
        services.AddScoped<Services.Startup.StartupCoordinator>();
        services.AddSingleton<Services.Startup.UpdaterRecoveryService>();

        // Reader mode - scaffold: interfaces registered, real bodies in step 2
        services.AddSingleton<Services.Reader.IReaderSanitizer, Services.Reader.ReaderSanitizer>();
        services.AddScoped<Services.Reader.IReaderExtractor, Services.Reader.ReaderExtractor>();
        services.AddSingleton<Services.Reader.IReaderTemplateRenderer, Services.Reader.ReaderTemplateRenderer>();
        services.AddScoped<Services.Reader.IReaderService, Services.Reader.ReaderService>();

        // Link preview - Alt plus click, on demand, sleep not hibernate
        services.AddSingleton<Services.LinkPreview.ILinkPreviewPolicy, Services.LinkPreview.LinkPreviewPolicy>();
        services.AddSingleton<Services.LinkPreview.ILinkPreviewDownloadSuppressor, Services.LinkPreview.LinkPreviewDownloadSuppressor>();
        // Scoped: the preview view model and its window belong to one window and are
        // dismissed when that window closes.
        services.AddScoped<Services.LinkPreview.LinkPreviewService>();
        services.AddScoped<Services.LinkPreview.ILinkPreviewService>(sp => sp.GetRequiredService<Services.LinkPreview.LinkPreviewService>());
        services.AddScoped<ViewModels.LinkPreview.LinkPreviewViewModel>();
        services.AddScoped<Services.UI.LinkPreviewWindowController>();

        // Process wide state shared by every window.
        services.AddSingleton<BrowserSession>();
        services.AddSingleton<Services.UI.BrowserWindowManager>();
        services.AddScoped<Services.UI.BrowserWindowContext>();

        // Engine dependencies record
        services.AddScoped(sp => new EngineDependencies
        {
            ExtensionManager = sp.GetRequiredService<ExtensionManager>(),
            YouTubeUnhook = sp.GetRequiredService<YouTubeUnhook>(),
            YouTubeEnhancer = sp.GetRequiredService<YouTubeEnhancer>(),
            Settings = sp.GetRequiredService<BrowserSettings>(),
            FaviconLoader = sp.GetRequiredService<FaviconLoader>(),
            Pages = sp.GetRequiredService<InternalPages>(),
            HistoryStore = sp.GetRequiredService<IHistoryStore>(),
            OneTabStore = sp.GetRequiredService<IOneTabStore>(),
            DownloadStore = sp.GetRequiredService<IDownloadStore>(),
            ClosedTabStore = sp.GetRequiredService<IClosedTabStore>(),
            FocusBlocklistService = sp.GetRequiredService<FocusBlocklistService>(),
            ContentScriptInjector = sp.GetRequiredService<ContentScriptInjector>(),
            CustomDownloadManager = sp.GetRequiredService<CustomDownloadManager>(),
            HibernationManager = sp.GetRequiredService<TabHibernationManager>(),
            NavigationPolicyEngine = sp.GetRequiredService<NavigationPolicyEngine>(),
            ThemeManager = sp.GetRequiredService<ThemeManager>(),
            DownloadSuppressor = sp.GetRequiredService<Services.LinkPreview.ILinkPreviewDownloadSuppressor>(),
            Session = sp.GetRequiredService<BrowserSession>()
        });
        // Scoped: one engine per window, so a tab can be moved between windows without
        // two engines claiming the same tab list.
        services.AddScoped<TabEngine>();
        
        // Message Handlers
        services.AddScoped<IWebMessageHandler, CoreMessageHandler>();
        services.AddScoped<IWebMessageHandler, SettingsMessageHandler>();
        services.AddScoped<IWebMessageHandler, OneTabMessageHandler>();
        services.AddScoped<IWebMessageHandler, HistoryMessageHandler>();
        services.AddScoped<IWebMessageHandler, ShortcutMessageHandler>();
        services.AddScoped<IWebMessageHandler, DownloadMessageHandler>();
        services.AddScoped<IWebMessageHandler, OnboardingMessageHandler>();
        services.AddScoped<IWebMessageHandler, TCLensMessageHandler>();

        services.AddScoped<WebMessageRouter>();
        services.AddSingleton<TCLensTransferService>();
        services.AddScoped<Services.UI.TCLensLauncher>();

        // ViewModel and Views
        services.AddScoped<BrowserViewModel>(sp =>
        {
            var settings = sp.GetRequiredService<BrowserSettings>();
            var navigation = sp.GetRequiredService<NavigationService>();
            var downloadStore = sp.GetRequiredService<IDownloadStore>();
            var engine = sp.GetRequiredService<TabEngine>();
            var readerService = sp.GetRequiredService<Services.Reader.IReaderService>();
            var readerViewModel = sp.GetRequiredService<ViewModels.Reader.ReaderViewModel>();
            var updateService = sp.GetRequiredService<UpdateService>();
            var suggestions = sp.GetRequiredService<ISuggestionProvider>();
            var faviconLoader = sp.GetRequiredService<FaviconLoader>();
            return new BrowserViewModel(settings, navigation, downloadStore, engine, readerService, readerViewModel, updateService, suggestions, faviconLoader);
        });
        services.AddScoped<ViewModels.Reader.ReaderViewModel>(sp =>
        {
            var readerService = sp.GetRequiredService<Services.Reader.IReaderService>();
            var engine = sp.GetRequiredService<TabEngine>();
            return new ViewModels.Reader.ReaderViewModel(readerService, () => engine.ActiveTab?.Id);
        });
        services.AddScoped<MainWindow>();

        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        
        // Eagerly resolve ThemeManager so it can apply the initial theme
        sp.GetRequiredService<ThemeManager>();

        return sp;
    }

    /// <summary>
    /// Binds one window scope to its engine. Called by the window manager for each new
    /// window, because these handlers are per engine and a singleton engine would keep
    /// only the last window's handlers.
    /// </summary>
    public static void WireWindowServices(IServiceProvider sp)
    {
        // Wire ReaderService cleanup on tab close and single-WebView reader guard
        var tabEngine = sp.GetRequiredService<TabEngine>();
        var readerService = sp.GetRequiredService<Services.Reader.IReaderService>();
        tabEngine.TabClosed += readerService.RemoveSession;
        
        var readerViewModel = sp.GetRequiredService<ViewModels.Reader.ReaderViewModel>();
        tabEngine.IsReaderAvailable = tabId => readerViewModel.IsReaderAvailable && tabEngine.ActiveTab?.Id == tabId;
        tabEngine.IsReaderActive = tabId => readerService.GetSession(tabId)?.IsInReader == true;
        tabEngine.ExitReaderAsync = tabId => readerService.ExitReaderAsync(tabId);
        tabEngine.ToggleReaderAsync = async tabId =>
        {
            if (tabEngine.ActiveTab?.Id == tabId)
                await readerViewModel.ToggleAsync();
        };

        // Wire Link Preview - on demand, Alt plus click, sleep not hibernate
        var linkPreviewService = sp.GetRequiredService<Services.LinkPreview.LinkPreviewService>();
        tabEngine.LinkPreviewRequested += (tab, url, rect, trigger) =>
        {
            linkPreviewService.RequestPeek(tab.Id, url, rect, trigger, tab.Url);
        };
        linkPreviewService.OriginShouldSleep += tabId => { _ = tabEngine.SuspendForPreviewAsync(tabId); };
        linkPreviewService.OriginShouldResume += tabId => tabEngine.ResumeFromPreview(tabId);
    }
}
