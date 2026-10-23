using System;

namespace UmberBrowser.Services.UI;

/// <summary>
/// The minimum tracking size a top level window must report in its
/// WM_GETMINMAXINFO reply. Windows clamps every interactive edge, corner and
/// snap resize to that reply, in device pixels. The window declares its
/// minimum in DIP, so the declared minimum only becomes real when
/// <see cref="OneAxis"/> converts it with the window's current scale, the
/// scale the window serves its content at right now, which follows the
/// monitor the window sits on.
/// </summary>
public static class WindowMinTrackSize
{
    /// <summary>
    /// One axis of the minimum track size in whole device pixels.
    ///
    /// A size that is not a positive finite number names no floor, and a scale
    /// that is not a positive finite number cannot convert one, so both yield
    /// the smallest legal floor of 1 px rather than an unusable 0. A finite
    /// conversion rounds up to the next whole pixel, so the reply never lands
    /// short of the declared minimum, and the result is clamped into the range
    /// the native POINT field can carry.
    /// </summary>
    public static int OneAxis(double minimumDip, double scale)
    {
        if (!Usable(minimumDip) || !Usable(scale)) return 1;
        var pixels = Math.Ceiling(minimumDip * scale);
        if (pixels >= int.MaxValue) return int.MaxValue;
        return Math.Max(1, (int)pixels);
    }

    private static bool Usable(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
}
