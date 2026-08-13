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

        ThemeManager.Initialize(settings);

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
        services.AddSingleton<InternalPages>();
        services.AddSingleton<YouTubeEnhancer>();
        services.AddSingleton<YouTubeUnhook>();
        services.AddSingleton<FocusBlocklistService>();
        services.AddSingleton<Engine.ContentScriptInjector>();
        services.AddSingleton<CustomDownloadManager>();
        services.AddSingleton<UpdateService>();

        // Engine dependencies record
        services.AddSingleton<EngineDependencies>();
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

        // ViewModel
        services.AddSingleton<BrowserViewModel>();

        return services.BuildServiceProvider();
    }
}
