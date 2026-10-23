namespace UmberBrowser.Helpers;

/// <summary>
/// Icon resources for the current build flavor. Debug builds use the violet
/// dev colorway. Release builds use the neutral production colorway.
/// </summary>
internal static class AppIconResources
{
    // Tab marks are transparent so the rings fill the favicon slot. TabDark is
    // the light mark shown on dark tabs, TabLight is the dark mark shown on
    // light tabs. Window icons and the About card keep the tiled artwork.
#if DEBUG
    public const string WindowDark = "pack://application:,,,/icons/umber-dev.ico";
    public const string WindowLight = "pack://application:,,,/icons/umber-dev-light.ico";
    public const string TabDark = "pack://application:,,,/icons/umber-mark-dev-light-32x32.png";
    public const string TabLight = "pack://application:,,,/icons/umber-mark-dev-dark-32x32.png";
    public const string AboutResource = "icons.umber-browser-dev-dark-128x128.png";
#else
    public const string WindowDark = "pack://application:,,,/icons/umber.ico";
    public const string WindowLight = "pack://application:,,,/icons/umber-light.ico";
    public const string TabDark = "pack://application:,,,/icons/umber-mark-light-32x32.png";
    public const string TabLight = "pack://application:,,,/icons/umber-mark-dark-32x32.png";
    public const string AboutResource = "icons.umber-browser-128x128.png";
#endif
}
