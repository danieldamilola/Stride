using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Engine;

namespace StrideBrowser.Controls;

public partial class LinkPreviewWindow : Window
{
    private dynamic? _webView;
    private CoreWebView2Environment? _environment;
    private string _currentUrl = string.Empty;
    private double _zoom = 1.0;

    public event Action? OpenInNewTabRequested;
    public event Action? OpenInCurrentTabRequested;
    public event Action? CloseRequested;
    public event Action<string>? PreviewLoaded;
    public event Action? PreviewCloseKey;

    public LinkPreviewWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += OnDeactivated;
    }

    public void SetEnvironment(CoreWebView2Environment env)
    {
        _environment = env;
    }

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.25, 5.0);
        try { if (_webView?.CoreWebView2 != null) _webView.ZoomFactor = _zoom; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    public void UpdateHeader(string url, string title)
    {
        Dispatcher.Invoke(() =>
        {
            UrlText.Text = url;
            TitleText.Text = string.IsNullOrWhiteSpace(title) ? url : title;
        });
    }

    private Task WaitForLoadedAsync()
    {
        if (IsLoaded) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        RoutedEventHandler handler = null!;
        handler = (s, e) =>
        {
            Loaded -= handler;
            tcs.TrySetResult();
        };
        Loaded += handler;
        return tcs.Task;
    }

    private bool _isInitializing;
    private bool _isInitialized;

    public async Task NavigateToAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        _currentUrl = url;
        UpdateHeader(url, "Loading...");

        await WaitForLoadedAsync();

        if (_webView is null)
        {
            _webView = new WebView2CompositionControl
            {
                DefaultBackgroundColor = System.Drawing.Color.White
            };
            PreviewHost.Children.Add((FrameworkElement)_webView);
        }

        if (!_isInitialized)
        {
            if (_isInitializing) return;
            _isInitializing = true;
            try
            {
                await _webView.EnsureCoreWebView2Async(_environment);

                try { _webView.ZoomFactor = _zoom; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }

                // Lock down preview WebView
                _webView.CoreWebView2.NewWindowRequested += new EventHandler<CoreWebView2NewWindowRequestedEventArgs>((_, e) => { e.Handled = true; });
                _webView.CoreWebView2.PermissionRequested += new EventHandler<CoreWebView2PermissionRequestedEventArgs>((_, e) =>
                {
                    e.State = CoreWebView2PermissionState.Deny;
                    e.Handled = true;
                });
                _webView.CoreWebView2.DownloadStarting += new EventHandler<CoreWebView2DownloadStartingEventArgs>((_, e) =>
                {
                    e.Cancel = true;
                    e.Handled = true;
                });

                _webView.CoreWebView2.DocumentTitleChanged += new EventHandler<object>((_, _) =>
                {
                    try { UpdateHeader(_currentUrl, _webView.CoreWebView2.DocumentTitle); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                });
                _webView.CoreWebView2.NavigationStarting += new EventHandler<CoreWebView2NavigationStartingEventArgs>((_, _) =>
                {
                    try { _webView.ZoomFactor = _zoom; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                });
                _webView.CoreWebView2.NavigationCompleted += new EventHandler<CoreWebView2NavigationCompletedEventArgs>((_, e) =>
                {
                    if (e.IsSuccess)
                    {
                        try { _webView.ZoomFactor = _zoom; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                        PreviewLoaded?.Invoke(_currentUrl);
                        try { UpdateHeader(_currentUrl, _webView.CoreWebView2.DocumentTitle); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                    }
                    else
                    {
                        try { UpdateHeader(_currentUrl, $"Failed to load: {e.WebErrorStatus}"); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                    }
                });
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"LinkPreview WebView init failed: {ex.Message}");
                UpdateHeader(_currentUrl, $"Preview error: {ex.Message}");
                return;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        try { _webView.ZoomFactor = _zoom; } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        try
        {
            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.Navigate(_currentUrl);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"LinkPreview navigate failed: {ex.Message}");
            UpdateHeader(_currentUrl, $"Navigation error: {ex.Message}");
        }
    }

    public void CleanupWebView()
    {
        try { _webView?.CoreWebView2?.Stop(); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        try { _webView?.CoreWebView2?.Navigate("about:blank"); } catch (System.Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        _currentUrl = string.Empty;
    }

    public void StopPreview()
    {
        CleanupWebView();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            PreviewCloseKey?.Invoke();
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible && IsLoaded)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsActive && IsVisible)
                    CloseRequested?.Invoke();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OpenNewTab_Click(object sender, RoutedEventArgs e) => OpenInNewTabRequested?.Invoke();
    private void OpenCurrent_Click(object sender, RoutedEventArgs e) => OpenInCurrentTabRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    protected override void OnClosed(EventArgs e)
    {
        StopPreview();
        base.OnClosed(e);
    }
}
