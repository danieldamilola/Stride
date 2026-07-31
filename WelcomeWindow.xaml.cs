using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace StrideBrowser;

public partial class WelcomeWindow : Window
{
    private BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IServiceProvider _services;

    public WelcomeWindow(IServiceProvider services)
    {
        InitializeComponent();

        _services = services;
        _settingsStore = services.GetRequiredService<ISettingsStore>();
        _settings = _settingsStore.Load();

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StrideBrowser", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        // Load the HTML from embedded resource (all .html files are EmbeddedResource)
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "StrideBrowser.Resources.Pages.Welcome.html";

        string html;
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                // Fallback: inline minimal HTML if resource not found
                html = BuildFallbackHtml();
            }
            else
            {
                using var reader = new StreamReader(stream);
                html = await reader.ReadToEndAsync();
            }
        }

        // Inject initial settings into the page before it loads
        var settingsJson = JsonSerializer.Serialize(_settings,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        html = html.Replace("</head>",
            $"<script>window.initialSettings = {settingsJson};</script></head>");

        webView.CoreWebView2.NavigateToString(html);
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (msg == null) return;

        if (msg.StartsWith("save:"))
        {
            var json = msg.Substring(5);
            var updatedSettings = JsonSerializer.Deserialize<BrowserSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (updatedSettings != null)
            {
                // Preserve shortcuts if they were imported
                updatedSettings.NewTabShortcuts = _settings.NewTabShortcuts;
                _settingsStore.Save(updatedSettings);

                // Build and show MainWindow
                Dispatcher.Invoke(() =>
                {
                    var vm = _services.GetRequiredService<BrowserViewModel>();
                    var mainWindow = new MainWindow(_services, vm);
                    mainWindow.Show();
                    this.Close();
                });
            }
        }
        else if (msg == "close")
        {
            Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }

    private static string BuildFallbackHtml() => """
        <!DOCTYPE html>
        <html>
        <head><meta charset="UTF-8"><title>Welcome to Stride</title>
        <style>
        * { margin:0; padding:0; box-sizing:border-box; }
        body { background:#111113; color:#E8E4DF; font-family:system-ui,sans-serif;
               display:flex; align-items:center; justify-content:center; height:100vh; }
        .box { text-align:center; padding:40px; }
        h1 { font-size:28px; font-weight:500; margin-bottom:12px; }
        p  { color:#A5A3A0; margin-bottom:28px; }
        button { background:#E3B341; color:#111; border:none; padding:12px 28px;
                 border-radius:10px; font-size:15px; cursor:pointer; }
        </style></head>
        <body>
        <div class="box">
          <h1>Welcome to Stride</h1>
          <p>A minimalist browser built for focus.</p>
          <button onclick="start()">Start Browsing</button>
        </div>
        <script>
        function start() {
          var s = window.initialSettings || {};
          s.isFirstRun = false;
          window.chrome.webview.postMessage('save:' + JSON.stringify(s));
        }
        </script>
        </body></html>
        """;
}
