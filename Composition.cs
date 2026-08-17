using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Engine;
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
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.CoreMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.SettingsMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.OneTabMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.HistoryMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.ShortcutMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.DownloadMessageHandler>();
        services.AddSingleton<StrideBrowser.Services.MessageHandlers.IWebMessageHandler, StrideBrowser.Services.MessageHandlers.TCLensMessageHandler>();

        services.AddSingleton<WebMessageRouter>();
        services.AddSingleton<TCLensTransferService>();

        // ViewModel and Views
        services.AddSingleton<BrowserViewModel>();
        services.AddTransient<MainWindow>();

        var sp = services.BuildServiceProvider();
        
        // Eagerly resolve ThemeManager so it can apply the initial theme
        sp.GetRequiredService<ThemeManager>();
        
        return sp;
    }
}
