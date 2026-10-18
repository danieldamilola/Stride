using System;

namespace StrideBrowser.Services.UI;

/// <summary>
/// The width each part of the toolbar row is built from, and the window widths at which the
/// row drops the controls it can no longer afford.
///
/// The toolbar is one 38 DIP row at every window width: navigation, the tab strip and the
/// address field, the action buttons, then the window buttons. Nothing moves to a second row.
/// As the window narrows the row gives up width in a fixed order, the order Helium's toolbar
/// uses, so the controls that carry navigation survive the smallest window:
///
///   1. the tab strip gives up width first, down to the new tab button and one tab card;
///   2. the address field gives up width next, down to <see cref="AddressMinWidth"/>;
///   3. the optional action buttons (downloads, reader, update) collapse into one overflow
///      button, which opens the same commands from a menu;
///   4. the overflow button goes, then the settings button;
///   5. the window buttons, the navigation buttons, the address field and the tab strip never
///      go. <see cref="MinWindowWidth"/> is the smallest window: 400 DIP, the floor Helium uses
///      for its own window, and at the same time the width at which the row still pays for the
///      address field at its floor and one tab card. <see cref="AbsoluteMinWidth"/> sits 32 DIP
///      below that, where the row would still lay out with the tab cards gone entirely.
///
/// Every number here mirrors MainWindow.xaml, so the rule is testable without a window. The
/// address width and the action mode are pure functions of the window width, which is what
/// <c>AddressFieldMaxWidthConverter</c> and <c>ToolbarActionVisibilityConverter</c> call.
/// </summary>
public static class ToolbarLayoutMetrics
{
    // ── Control sizes, mirroring MainWindow.xaml ──

    /// <summary>Left margin on the toolbar row.</summary>
    public const double RowMargin = 6;

    /// <summary>Back, forward and refresh.</summary>
    public const double NavButton = 28;

    /// <summary>Gap between two navigation buttons.</summary>
    public const double NavGap = 3;

    /// <summary>Trailing margin on the navigation group.</summary>
    public const double NavTrailingMargin = 2;

    /// <summary>Downloads, reader, update, settings.</summary>
    public const double ActionButton = 28;

    /// <summary>Gap between two action buttons.</summary>
    public const double ActionGap = 3;

    /// <summary>Trailing margin on the action cluster, which sits next to the window buttons.</summary>
    public const double ActionsTrailingMargin = 6;

    /// <summary>Minimize, maximize and close.</summary>
    public const double WindowButton = 42;

    /// <summary>The new tab button in the strip.</summary>
    public const double NewTabWidth = 28;

    /// <summary>Trailing margin on the new tab button.</summary>
    public const double NewTabMargin = 10;

    /// <summary>Gap between the address field and the tab strip.</summary>
    public const double AddressGap = 6;

    /// <summary>Navigation buttons, left to right, at their full 28 DIP.</summary>
    public const double NavWidth = NavButton * 3 + NavGap * 2 + NavTrailingMargin;

    /// <summary>Minimize, maximize and close.</summary>
    public const double WindowControlsWidth = WindowButton * 3;

    /// <summary>An address field still worth typing in: 8 DIP of padding and 84 DIP of text.</summary>
    public const double AddressMinWidth = 100;

    /// <summary>The field's width while the window has room for it.</summary>
    public const double AddressMaxWidth = 200;

    /// <summary>The new tab button and one tab card: the strip never goes below this.</summary>
    public const double MinTabStrip = NewTabWidth + NewTabMargin + TabShrinkPanel.IconTabWidth;

    /// <summary>Downloads and reader at full size. The update button is transient and not budgeted.</summary>
    public const double OptionalActionsWidth = ActionButton * 2 + ActionGap * 2;

    /// <summary>The overflow button that stands in for the optional buttons.</summary>
    public const double OverflowButtonWidth = ActionButton + ActionGap;

    /// <summary>The settings button, which keeps its place longest, plus the trailing margin.</summary>
    public const double SettingsActionWidth = ActionButton + ActionsTrailingMargin;

    /// <summary>Action cluster while every button is in the row.</summary>
    public const double FullActionsColumn = OptionalActionsWidth + SettingsActionWidth;

    /// <summary>Action cluster while the optional buttons are behind the overflow button.</summary>
    public const double OverflowActionsColumn = OverflowButtonWidth + SettingsActionWidth;

    /// <summary>The address field at its floor, the gap, and the tab strip at its minimum.</summary>
    public const double CentreMinWidth = AddressMinWidth + AddressGap + MinTabStrip;

    /// <summary>The row keeps only the controls it cannot do without: navigation, the address
    /// field, the tab strip and the window buttons.</summary>
    public const double MinWindowWidth = RowMargin + NavWidth + WindowControlsWidth + CentreMinWidth;

    /// <summary>
    /// The row with the address field at its floor and nothing but the new tab button beside it.
    /// The tab cards go to zero before this, so it sits below <see cref="MinWindowWidth"/>: it is
    /// the width at which the row stops fitting at all, not the width the window stops at.
    /// </summary>
    public const double AbsoluteMinWidth =
        RowMargin + NavWidth + WindowControlsWidth + AddressMinWidth + AddressGap + NewTabWidth + NewTabMargin;

    /// <summary>Below this the optional buttons fold into the overflow button.</summary>
    public const double FullActionsMinWindow =
        RowMargin + NavWidth + FullActionsColumn + WindowControlsWidth + CentreMinWidth;

    /// <summary>Below this the overflow button goes too and only settings is left.</summary>
    public const double OverflowMinWindow =
        RowMargin + NavWidth + OverflowActionsColumn + WindowControlsWidth + CentreMinWidth;

    /// <summary>Below this the row carries no action button at all.</summary>
    public const double SettingsMinWindow =
        RowMargin + NavWidth + SettingsActionWidth + WindowControlsWidth + CentreMinWidth;

    /// <summary>
    /// Which action controls the row has room for at this window width. The thresholds are the
    /// widths at which the action cluster, the address field at its floor and the tab strip at
    /// its minimum stop fitting side by side with the navigation and window buttons.
    /// </summary>
    public static ToolbarActionsMode ActionsMode(double windowWidth)
    {
        if (!double.IsFinite(windowWidth)) return ToolbarActionsMode.None;
        if (windowWidth >= FullActionsMinWindow) return ToolbarActionsMode.Full;
        if (windowWidth >= OverflowMinWindow) return ToolbarActionsMode.Overflow;
        if (windowWidth >= SettingsMinWindow) return ToolbarActionsMode.SettingsOnly;
        return ToolbarActionsMode.None;
    }

    /// <summary>The width the action cluster takes at this window width.</summary>
    public static double ActionsColumn(double windowWidth) => ActionsMode(windowWidth) switch
    {
        ToolbarActionsMode.Full => FullActionsColumn,
        ToolbarActionsMode.Overflow => OverflowActionsColumn,
        ToolbarActionsMode.SettingsOnly => SettingsActionWidth,
        _ => 0,
    };

    /// <summary>The width left in the middle of the row for the address field and tab strip.</summary>
    public static double CentreWidth(double windowWidth) =>
        windowWidth - RowMargin - NavWidth - ActionsColumn(windowWidth) - WindowControlsWidth;

    /// <summary>
    /// The address field width for a window: the field takes the room the middle of the row has
    /// left once the tab strip has its minimum, and it gives width back to the strip as the
    /// window narrows, never below <see cref="AddressMinWidth"/>. Above
    /// <see cref="FullActionsMinWindow"/> this is <see cref="AddressMaxWidth"/>, so a roomy
    /// window lays out exactly as it did before the row became responsive.
    /// </summary>
    public static double AddressWidth(double windowWidth)
    {
        if (!double.IsFinite(windowWidth) || windowWidth <= 0) return AddressMinWidth;
        var remaining = CentreWidth(windowWidth) - AddressGap - MinTabStrip;
        return Math.Clamp(remaining, AddressMinWidth, AddressMaxWidth);
    }

    /// <summary>
    /// True while the row still fits the controls it never drops: the address field at its
    /// floor, the gap, and the new tab button. The tab cards themselves may go to zero, the
    /// strip scrolls, so one card is not part of this bound.
    /// </summary>
    public static bool ToolbarFits(double windowWidth) =>
        double.IsFinite(windowWidth) && windowWidth > 0 &&
        CentreWidth(windowWidth) >= AddressMinWidth + AddressGap + NewTabWidth + NewTabMargin;
}

/// <summary>How much of the action cluster the toolbar row has room for.</summary>
public enum ToolbarActionsMode
{
    /// <summary>Navigation, the address field, the tab strip and the window buttons only.</summary>
    None,

    /// <summary>The settings button, which is the last action control to go.</summary>
    SettingsOnly,

    /// <summary>The settings button and the overflow button that holds the optional commands.</summary>
    Overflow,

    /// <summary>Every action button in the row.</summary>
    Full,
}
