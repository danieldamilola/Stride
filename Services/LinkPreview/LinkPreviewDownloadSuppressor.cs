using System.Collections.Concurrent;

namespace StrideBrowser.Services.LinkPreview;

public interface ILinkPreviewDownloadSuppressor
{
    void Add(string url);
    bool ShouldSuppress(string url);
}

public class LinkPreviewDownloadSuppressor : ILinkPreviewDownloadSuppressor
{
    private readonly ConcurrentDictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        _pending[url.Trim()] = DateTime.UtcNow.AddSeconds(3);
        Cleanup();
    }

    public bool ShouldSuppress(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        Cleanup();
        var key = url.Trim();
        if (_pending.TryGetValue(key, out var expiry))
        {
            if (DateTime.UtcNow <= expiry) return true;
            _pending.TryRemove(key, out _);
        }
        // also check without fragment
        if (Uri.TryCreate(key, UriKind.Absolute, out var uri))
        {
            var withoutFragment = uri.GetLeftPart(UriPartial.Path);
            if (_pending.TryGetValue(withoutFragment, out expiry) && DateTime.UtcNow <= expiry) return true;
        }
        return false;
    }

    private void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _pending)
        {
            if (kvp.Value < now) _pending.TryRemove(kvp.Key, out _);
        }
    }
}
