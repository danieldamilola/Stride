using System;
using System.Windows;

namespace StrideBrowser.Services.UI;

/// <summary>
/// The measured geometry of the compact rounded frame around the web content.
///
/// The window edge is the origin. On the sides and the bottom a gap of
/// <see cref="GapDip"/> shows the frame surface, then the <see cref="StrokeDip"/>
/// outline, then the web content. The top has no gap: the toolbar strip ends where
/// the outline starts, because the same surface under a centred tab card reads as a
/// second strip of dead space. The outline is a rounded rectangle with
/// <see cref="OutlineRadiusDip"/> on its outer edge, so its inner edge and the content
/// corner are the same arc at <see cref="ContentRadiusDip"/>. Keeping the two radii one
/// stroke apart is what stops a square sliver of the host peeking past the corner.
///
/// All values are DIPs because WPF lays the margin out in DIPs. The native region
/// clip is binary and integer only, so the content radius is converted per window
/// DPI by <see cref="ContentRadiusPx"/>.
/// </summary>
public static class RoundedWebContentFrameGeometry
{
    /// <summary>Frame surface between the window edge and the outline, on the sides and the bottom.</summary>
    public const double GapDip = 3.0;

    /// <summary>
    /// The same surface at the top, kept at zero so the outline begins straight under
    /// the toolbar and the page sits closer to it.
    /// </summary>
    public const double TopGapDip = 0.0;

    /// <summary>Thin outline stroke drawn over the gap.</summary>
    public const double StrokeDip = 1.0;

    /// <summary>Distance from the window edge to the web content, on the sides and the bottom.</summary>
    public const double ContentInsetDip = GapDip + StrokeDip;

    /// <summary>Distance from the toolbar to the web content. The outline stroke still sits in it.</summary>
    public const double TopContentInsetDip = TopGapDip + StrokeDip;

    /// <summary>Outer radius of the outline stroke.</summary>
    public const double OutlineRadiusDip = 8.0;

    /// <summary>Corner radius of the web content, concentric with the outline inner edge.</summary>
    public const double ContentRadiusDip = OutlineRadiusDip - StrokeDip;

    /// <summary>
    /// True when the frame is drawn. Fullscreen is <see cref="ResizeMode.NoResize"/>,
    /// and a minimized window has nothing to draw, so both drop the frame. Maximized
    /// keeps it: a maximized page still fills the window and its corners still need
    /// clipping. The resize border is not in use at that size, so the frame does not
    /// wait on it.
    /// </summary>
    public static bool IsFrameOn(bool enabled, WindowState state, ResizeMode mode) =>
        enabled &&
        mode != ResizeMode.NoResize &&
        (state == WindowState.Normal || state == WindowState.Maximized);

    /// <summary>
    /// The outline bounds. One gap in from the sides and the bottom, and flush to the
    /// top, so the outline outer edge begins where the toolbar ends.
    /// </summary>
    public static Thickness OutlineMargin() => new(GapDip, TopGapDip, GapDip, GapDip);

    /// <summary>
    /// The page host margin: the outline bounds plus the stroke on every edge. The stroke
    /// lands between the frame surface and the page on every edge, so the outline inner
    /// edge and the page edge are the same rectangle and the corner arcs stay concentric.
    /// </summary>
    public static Thickness ContentMargin() =>
        new(ContentInsetDip, TopContentInsetDip, ContentInsetDip, ContentInsetDip);

    /// <summary>The outline stroke width.</summary>
    public static Thickness OutlineThickness() => new(StrokeDip);

    /// <summary>The outline outer corner radius.</summary>
    public static CornerRadius OutlineCornerRadius() => new(OutlineRadiusDip);

    /// <summary>
    /// The web content corner radius in physical pixels for a window at
    /// <paramref name="dpi"/>. Rounded away from zero so a fractional scale keeps a
    /// visible arc instead of collapsing the corner to a square.
    /// </summary>
    public static int ContentRadiusPx(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0) dpi = 96.0;
        var px = (int)Math.Round(ContentRadiusDip * dpi / 96.0, MidpointRounding.AwayFromZero);
        return px < 0 ? 0 : px;
    }

    /// <summary>
    /// The ellipse diameter <c>CreateRoundRectRgn</c> wants. Clamped to the smaller
    /// host side so a host narrower than the radius still gets a valid region.
    /// </summary>
    public static int RegionEllipsePx(int radiusPx, int widthPx, int heightPx)
    {
        var diameter = radiusPx * 2;
        var limit = Math.Min(widthPx, heightPx);
        if (limit < 0) limit = 0;
        if (diameter > limit) diameter = limit;
        return diameter < 0 ? 0 : diameter;
    }
}
