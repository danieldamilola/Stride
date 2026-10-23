using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UmberBrowser.Engine;
using UmberBrowser.Models;

namespace UmberBrowser.Services.UI;

/// <summary>Hosts an extension action in a toolbar popup using the current browser profile.</summary>
public sealed class ExtensionActionPopup : IDisposable
{
    private readonly Window _owner;
    private readonly TabEngine _engine;
    private readonly FrameworkElement _anchor;
    private readonly Popup _popup;
    private readonly Border _frame;
    private readonly WebView2CompositionControl _view = new();
    private bool _disposed;
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _measuring;
    public bool IsOpen => _popup.IsOpen;

    public ExtensionActionPopup(Window owner, TabEngine engine, FrameworkElement anchor)
    {
        _owner = owner; _engine = engine; _anchor = anchor;
        _frame = new Border { BorderThickness = new Thickness(1), Child = _view, Width = 320, Height = 400 };
        _frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        _popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Custom,
            StaysOpen = true, AllowsTransparency = false, Child = _frame
        };
        _popup.CustomPopupPlacementCallback = (size, target, _) => DownloadsPopupController.GetPlacements(
            size, target, anchor.TranslatePoint(new Point(), owner).X, owner.ActualWidth);
        _sizeTimer.Tick += OnTick;
        _popup.Closed += Closed;
        _popup.PreviewKeyDown += KeyDown;
        owner.Deactivated += Deactivated; owner.SizeChanged += Resized; owner.LocationChanged += Moved;
        owner.Closed += OwnerClosed; engine.ActiveTabChanged += ActiveTabChanged;
        owner.PreviewMouseDown += OwnerMouseDown;
    }

    public async Task ShowAsync(CoreWebView2Environment environment, string profileName, Uri uri, bool developerMode, Action<string> openExternal, string? targetUrl = null, string? targetTitle = null)
    {
        _popup.IsOpen = true;
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = profileName;
            await _view.EnsureCoreWebView2Async(environment, options);
            if (_disposed) return;
            var core = _view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = developerMode;
            core.Settings.IsWebMessageEnabled = false;
            var resolvingTarget = targetUrl is not null;
            core.NavigationCompleted += async (_, e) =>
            {
                if (_disposed || !e.IsSuccess) return;
                try
                {
                    if (resolvingTarget)
                    {
                        resolvingTarget = false;
                        await core.ExecuteScriptAsync("globalThis.__strideTargetTabs=null;chrome.tabs.query({}).then(t=>globalThis.__strideTargetTabs=t)");
                        for (var attempt = 0; attempt < 20 && !_disposed; attempt++)
                        {
                            var tabs = await core.ExecuteScriptAsync("globalThis.__strideTargetTabs");
                            var tabId = FindTargetTabId(tabs, targetUrl!, targetTitle);
                            if (tabId is not null)
                            {
                                var targeted = new UriBuilder(uri) { Query = "tabId=" + tabId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                                core.Navigate(targeted.Uri.AbsoluteUri);
                                return;
                            }
                            await Task.Delay(50);
                        }
                    }
                    if (_disposed) return;
                    await core.ExecuteScriptAsync("document.documentElement.style.width='fit-content';document.documentElement.style.height='fit-content';document.body.style.width='fit-content'");
                    _sizeTimer.Start();
                    _ = MeasureAsync();
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            };
            if (_disposed) return;
            core.NavigationStarting += (_, e) =>
            {
                if (SameExtension(e.Uri, uri)) return;
                e.Cancel = true; openExternal(e.Uri); Dispose();
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; openExternal(e.Uri); Dispose(); };
            core.Navigate(uri.AbsoluteUri);
            _view.Focus();
        }
        catch { Dispose(); throw; }
    }
    private void OnTick(object? sender, EventArgs e) => _ = MeasureAsync();
    private async Task MeasureAsync()
    {
        if (_disposed || _measuring || _view.CoreWebView2 is null) return;
        _measuring = true;
        try
        {
            var json = await _view.CoreWebView2.ExecuteScriptAsync("(()=>{const b=document.body;if(!b)return null;const r=b.getBoundingClientRect();return {width:Math.ceil(Math.max(r.width,b.scrollWidth)),height:Math.ceil(Math.max(r.height,b.scrollHeight))};})()");
            if (_disposed) return;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            var size = ConstrainSize(doc.RootElement.GetProperty("width").GetDouble(), doc.RootElement.GetProperty("height").GetDouble(),
                _owner.ActualWidth, _owner.ActualHeight - _anchor.TranslatePoint(new Point(0, _anchor.ActualHeight), _owner).Y);
            _frame.Width = size.Width + 2; _frame.Height = size.Height + 2;
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.Message); }
        finally { _measuring = false; }
    }
    internal static int? FindTargetTabId(string json, string targetUrl, string? targetTitle)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return doc.RootElement.EnumerateArray()
                .Where(tab => tab.TryGetProperty("url", out var url) && url.GetString() == targetUrl && tab.TryGetProperty("id", out var id) && id.TryGetInt32(out _))
                .OrderByDescending(tab => tab.TryGetProperty("title", out var title) && title.GetString() == targetTitle)
                .ThenByDescending(tab => tab.TryGetProperty("lastAccessed", out var access) && access.TryGetDouble(out var time) ? time : 0)
                .Select(tab => (int?)tab.GetProperty("id").GetInt32()).FirstOrDefault();
        }
        catch (JsonException) { return null; }
    }
    private static bool SameExtension(string source, Uri uri) => Uri.TryCreate(source, UriKind.Absolute, out var next) && next.Scheme == uri.Scheme && next.Host == uri.Host;
    public static Size ConstrainSize(double width, double height, double ownerWidth, double availableHeight)
    {
        var maxWidth = double.IsFinite(ownerWidth) ? Math.Clamp(ownerWidth - 20, 80, 800) : 320;
        var maxHeight = double.IsFinite(availableHeight) ? Math.Clamp(availableHeight - 12, 60, 600) : 400;
        return new Size(Math.Clamp(double.IsFinite(width) ? width : 320, 80, maxWidth),
            Math.Clamp(double.IsFinite(height) ? height : 400, 60, maxHeight));
    }
    private void Closed(object? sender, EventArgs e) => Dispose();
    private void KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Dispose(); e.Handled = true; } }
    private void Deactivated(object? sender, EventArgs e) => Dispose();
    private void Resized(object sender, SizeChangedEventArgs e) => Dispose();
    private void Moved(object? sender, EventArgs e) => Dispose();
    private void OwnerClosed(object? sender, EventArgs e) => Dispose();
    private void OwnerMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Composition views can release mouse capture during navigation. Close on
        // input in the main window instead of treating that release as an outside click.
        if (e.OriginalSource is System.Windows.Media.Visual source &&
            PresentationSource.FromVisual(source) == PresentationSource.FromVisual(_owner)) Dispose();
    }
    private void ActiveTabChanged(BrowserTab? tab) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _sizeTimer.Stop(); _sizeTimer.Tick -= OnTick; _popup.Closed -= Closed; _popup.IsOpen = false; _popup.PreviewKeyDown -= KeyDown;
        _popup.CustomPopupPlacementCallback = null;
        _owner.Deactivated -= Deactivated; _owner.SizeChanged -= Resized; _owner.LocationChanged -= Moved;
        _owner.Closed -= OwnerClosed; _engine.ActiveTabChanged -= ActiveTabChanged;
        _owner.PreviewMouseDown -= OwnerMouseDown;
        _view.Dispose(); _frame.Child = null; _popup.Child = null;
    }
}
