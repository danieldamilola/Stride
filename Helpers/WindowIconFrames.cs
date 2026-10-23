namespace UmberBrowser.Helpers;

/// <summary>
/// Frame selection for multi-size window icons. A BitmapImage on an .ico
/// decodes frame 0, which the generator writes smallest, and the taskbar only
/// upscales what the window hands it. The decoder exposes every frame, so
/// callers pick the frame nearest the rendered size instead.
/// </summary>
public static class WindowIconFrames
{
    /// <summary>
    /// The big window icon renders at 32 logical pixels, scaled by the
    /// window's DPI.
    /// </summary>
    public static int TargetSize(double dpiScale, int logicalSize = 32) =>
        Math.Max(1, (int)Math.Round(logicalSize * dpiScale));

    /// <summary>
    /// Returns the index of the frame whose size is closest to the target.
    /// A tie goes to the larger frame so the taskbar never receives an
    /// upscaled small frame.
    /// </summary>
    public static int SelectIndex(IReadOnlyList<int> frameSizes, int target)
    {
        if (frameSizes.Count == 0)
            throw new ArgumentException("the icon decoder exposed no frames", nameof(frameSizes));

        var best = 0;
        for (var i = 1; i < frameSizes.Count; i++)
        {
            var candidate = Math.Abs(frameSizes[i] - target);
            var current = Math.Abs(frameSizes[best] - target);
            if (candidate > current) continue;
            // Strictly better or equal wins, so equal distance moves to the
            // larger frame because sizes list ascending order means i > best.
            best = i;
        }
        return best;
    }
}
