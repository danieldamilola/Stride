namespace UmberBrowser.Services.Downloads;

public sealed class DownloadProgressTracker
{
    private TimeSpan _lastTime;
    private long _lastBytes;
    private double _rate;
    public void Reset(long bytes, TimeSpan elapsed) { _lastBytes = bytes; _lastTime = elapsed; _rate = 0; }
    public (long Speed, TimeSpan? Remaining) Sample(long bytes, long total, TimeSpan elapsed)
    {
        var seconds = (elapsed - _lastTime).TotalSeconds;
        if (seconds < 0 || bytes < _lastBytes) { Reset(bytes, elapsed); return (0, null); }
        if (seconds >= 0.25)
        {
            var sample = (bytes - _lastBytes) / seconds;
            _rate = _rate == 0 ? sample : _rate * 0.5 + sample * 0.5;
            _lastTime = elapsed; _lastBytes = bytes;
        }
        var speed = (long)Math.Clamp(_rate, 0, long.MaxValue);
        TimeSpan? remaining = total > bytes && speed > 0
            ? TimeSpan.FromSeconds(Math.Min((double)(total - bytes) / speed, TimeSpan.MaxValue.TotalSeconds - 1)) : null;
        return (speed, remaining);
    }
}
