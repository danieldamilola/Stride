using System.Windows;
using StrideBrowser.Models;
using StrideBrowser.Models.LinkPreview;

namespace StrideBrowser.Services.LinkPreview;

/// <summary>
/// On demand peek service. No background timer. No polling.
/// Holds state for the current peek. Deduplicates rapid duplicate requests.
/// Notifies origin tab sleep via events for controller to handle.
/// </summary>
public sealed class LinkPreviewService : ILinkPreviewService
{
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly ILinkPreviewPolicy _policy;
    private readonly ILinkPreviewDownloadSuppressor _suppressor;
    private LinkPreviewState _current = LinkPreviewState.Hidden;
    private string? _lastUrl;
    private DateTime _lastPeekAt = DateTime.MinValue;
    private LinkPreviewOptions _options;

    public LinkPreviewService(BrowserSettings settings, ISettingsStore settingsStore, ILinkPreviewPolicy policy, ILinkPreviewDownloadSuppressor suppressor)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _policy = policy;
        _suppressor = suppressor;
        _options = new LinkPreviewOptions(
            Enabled: _settings.LinkPreviewEnabled,
            Hotkey: _settings.LinkPreviewHotkey ?? "Alt",
            AllowPress: _settings.LinkPreviewAllowPress
        );
        if (string.IsNullOrWhiteSpace(_options.Hotkey)) _options = _options with { Hotkey = "Alt" };
    }

    public LinkPreviewState Current => _current;
    public LinkPreviewOptions Options => _options;
    public bool IsPreviewVisible => _current.IsVisible;
    public Guid? ActiveOriginTabId => _current.IsVisible ? _current.TabId : null;

    public event Action<LinkPreviewState>? StateChanged;
    public event Action<Guid>? OriginShouldSleep;
    public event Action<Guid>? OriginShouldResume;

    public bool RequestPeek(Guid tabId, string url, Rect anchorRect, LinkPreviewTrigger trigger, string currentTabUrl)
    {
        if (tabId == Guid.Empty) return false;
        var trimmed = url?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed)) return false;

        // Deduplicate rapid duplicate peek for same url within 300 ms
        if (_current.IsVisible && string.Equals(_current.Url, trimmed, StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - _lastPeekAt).TotalMilliseconds < 300)
            return true;
        if (!string.Equals(_lastUrl, trimmed, StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - _lastPeekAt).TotalMilliseconds < 100)
            return false;

        if (!_policy.IsPreviewableUrl(trimmed, currentTabUrl)) return false;

        var request = new LinkPreviewRequest(tabId, trimmed, anchorRect, trigger, DateTime.UtcNow);
        if (!_policy.ShouldPeek(request, _options)) return false;

        // If already visible for different url, dismiss previous origin first
        var previousTab = _current.IsVisible ? _current.TabId : Guid.Empty;
        if (previousTab != Guid.Empty && previousTab != tabId)
        {
            OriginShouldResume?.Invoke(previousTab);
        }

        var position = new Point(0, 0);

        var next = new LinkPreviewState(
            IsVisible: true,
            Url: trimmed,
            AnchorRect: anchorRect,
            Position: position,
            Size: _current.Size,
            IsLoading: true,
            Trigger: trigger,
            TabId: tabId
        );

        _current = next;
        _lastUrl = trimmed;
        _lastPeekAt = DateTime.UtcNow;
        _suppressor.Add(trimmed);
        StateChanged?.Invoke(_current);
        OriginShouldSleep?.Invoke(tabId);

        return true;
    }

    public void Dismiss()
    {
        if (!_current.IsVisible) return;
        var origin = _current.TabId;
        _current = LinkPreviewState.Hidden;
        StateChanged?.Invoke(_current);
        if (origin != Guid.Empty)
            OriginShouldResume?.Invoke(origin);
    }

    public void NotifyPreviewLoaded(string url)
    {
        if (!_current.IsVisible) return;
        if (!string.Equals(_current.Url, url?.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        var updated = _current with { IsLoading = false };
        _current = updated;
        StateChanged?.Invoke(_current);
    }

    public void UpdateOptions(LinkPreviewOptions options)
    {
        var normalized = options;
        if (string.IsNullOrWhiteSpace(normalized.Hotkey)) normalized = normalized with { Hotkey = "Alt" };
        _options = normalized;
        _settings.LinkPreviewEnabled = normalized.Enabled;
        _settings.LinkPreviewHotkey = normalized.Hotkey;
        _settings.LinkPreviewAllowPress = normalized.AllowPress;
        _settingsStore.Save(_settings);
    }

    public void UpdatePreviewSize(Size size)
    {
        if (double.IsNaN(size.Width) || double.IsNaN(size.Height)) return;
        if (size.Width < 360 || size.Height < 240) return;
        _current = _current with { Size = size };
        StateChanged?.Invoke(_current);
    }
}
