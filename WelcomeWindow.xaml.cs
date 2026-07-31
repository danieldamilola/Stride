using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StrideBrowser;

public partial class WelcomeWindow : Window
{
    private BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;

    public WelcomeWindow()
    {
        InitializeComponent();
        
        _settingsStore = ((App)Application.Current).Services.GetRequiredService<ISettingsStore>();
        _settings = _settingsStore.Load();

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StrideBrowser", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "local.assets", 
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Pages"), 
            CoreWebView2HostResourceAccessKind.Allow);

        webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        
        // Pass initial settings
        var settingsJson = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var initScript = $"window.initialSettings = {settingsJson};";
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initScript);

        webView.CoreWebView2.Navigate("https://local.assets/Welcome.html");
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (msg == null) return;

        if (msg.StartsWith("save:"))
        {
            var json = msg.Substring(5);
            var updatedSettings = JsonSerializer.Deserialize<BrowserSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (updatedSettings != null)
            {
                // Preserve shortcuts if they were imported
                updatedSettings.NewTabShortcuts = _settings.NewTabShortcuts;
                
                // Save and launch
                _settingsStore.Save(updatedSettings);
                
                var mainWindow = ((App)Application.Current).Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
                this.Close();
            }
        }
        else if (msg == "close")
        {
            Application.Current.Shutdown();
        }
    }
}
