using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using StrideBrowser.Services.Input;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Covers the arithmetic the responsive single row toolbar is built on. The window minimum, the
/// address field width and the widths at which the action buttons fold into the overflow button
/// are all derived from the control widths, so each one is checked against the row it comes from
/// rather than asserted as a number a maintainer would have to trust.
/// </summary>
public sealed class ToolbarLayoutMetricsTests
{
    [Fact]
    public void MinWindowWidth_IsDerivedFromTheControlsTheRowKeeps()
    {
        var expected = ToolbarLayoutMetrics.RowMargin
            + ToolbarLayoutMetrics.NavWidth
            + ToolbarLayoutMetrics.WindowControlsWidth
            + ToolbarLayoutMetrics.CentreMinWidth;

        Assert.Equal(expected, ToolbarLayoutMetrics.MinWindowWidth);
        // Helium's own floor for the browser window is 400 DIP, and the row lands on it exactly,
        // so the derived minimum costs nothing beyond the reference.
        Assert.Equal(400, ToolbarLayoutMetrics.MinWindowWidth);
    }

    [Fact]
    public void MinWindowWidth_IsTheSmallestWidthTheRowLaysOutIn()
    {
        // At the minimum the row is exactly full: the address field at its floor and one tab card
        // beside the new tab button.
        Assert.Equal(ToolbarLayoutMetrics.CentreMinWidth,
            ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.MinWindowWidth), 3);
        Assert.True(ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.MinWindowWidth - 1)
            < ToolbarLayoutMetrics.CentreMinWidth,
            "one DIP below the minimum no longer pays for a tab card");

        // The row itself would still lay out 32 DIP narrower, with the tab cards gone.
        Assert.Equal(368, ToolbarLayoutMetrics.AbsoluteMinWidth);
        Assert.True(ToolbarLayoutMetrics.ToolbarFits(ToolbarLayoutMetrics.AbsoluteMinWidth));
        Assert.False(ToolbarLayoutMetrics.ToolbarFits(ToolbarLayoutMetrics.AbsoluteMinWidth - 1));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(440)]
    [InlineData(480)]
    [InlineData(600)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(1280)]
    [InlineData(1920)]
    public void ToolbarFits_AcrossEveryWindowWidthTheRowMustServe(double width)
    {
        Assert.True(ToolbarLayoutMetrics.ToolbarFits(width), $"{width} DIP must lay out without overlap");
    }

    [Fact]
    public void ActionLadder_DropsOneControlAtATime()
    {
        Assert.Equal(ToolbarActionsMode.None,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.MinWindowWidth));
        Assert.Equal(ToolbarActionsMode.SettingsOnly,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.SettingsMinWindow));
        Assert.Equal(ToolbarActionsMode.Overflow,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.OverflowMinWindow));
        Assert.Equal(ToolbarActionsMode.Full,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.FullActionsMinWindow));

        // And each step down really is one step.
        Assert.Equal(ToolbarActionsMode.None,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.SettingsMinWindow - 1));
        Assert.Equal(ToolbarActionsMode.SettingsOnly,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.OverflowMinWindow - 1));
        Assert.Equal(ToolbarActionsMode.Overflow,
            ToolbarLayoutMetrics.ActionsMode(ToolbarLayoutMetrics.FullActionsMinWindow - 1));
    }

    [Fact]
    public void ActionLadder_HoldsTheLadderStrictlyIncreasing()
    {
        Assert.True(ToolbarLayoutMetrics.MinWindowWidth < ToolbarLayoutMetrics.SettingsMinWindow);
        Assert.True(ToolbarLayoutMetrics.SettingsMinWindow < ToolbarLayoutMetrics.OverflowMinWindow);
        Assert.True(ToolbarLayoutMetrics.OverflowMinWindow < ToolbarLayoutMetrics.FullActionsMinWindow);
    }

    [Fact]
    public void ActionLadder_EveryStepLandsTheMiddleOfTheRowOnItsMinimum()
    {
        // Each threshold is the width at which the action cluster of that step stops fitting
        // beside the address field at its floor and the tab strip at its minimum.
        Assert.Equal(ToolbarLayoutMetrics.CentreMinWidth,
            ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.MinWindowWidth), 3);
        Assert.Equal(ToolbarLayoutMetrics.CentreMinWidth,
            ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.SettingsMinWindow), 3);
        Assert.Equal(ToolbarLayoutMetrics.CentreMinWidth,
            ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.OverflowMinWindow), 3);
        Assert.Equal(ToolbarLayoutMetrics.CentreMinWidth,
            ToolbarLayoutMetrics.CentreWidth(ToolbarLayoutMetrics.FullActionsMinWindow), 3);
    }

    [Fact]
    public void ActionColumns_AreBuiltFromTheButtonWidths()
    {
        var optional = (ToolbarLayoutMetrics.ActionButton + ToolbarLayoutMetrics.ActionGap) * 6;
        var settings = ToolbarLayoutMetrics.ActionButton + ToolbarLayoutMetrics.ActionsTrailingMargin;
        var overflow = ToolbarLayoutMetrics.ActionButton + ToolbarLayoutMetrics.ActionGap;

        Assert.Equal(optional, ToolbarLayoutMetrics.OptionalActionsWidth);
        Assert.Equal(settings, ToolbarLayoutMetrics.SettingsActionWidth);
        Assert.Equal(overflow, ToolbarLayoutMetrics.OverflowButtonWidth);
        Assert.Equal(optional + settings, ToolbarLayoutMetrics.FullActionsColumn);
        Assert.Equal(overflow + settings, ToolbarLayoutMetrics.OverflowActionsColumn);
    }

    [Fact]
    public void ActionColumn_FollowsTheMode()
    {
        Assert.Equal(0, ToolbarLayoutMetrics.ActionsColumn(ToolbarLayoutMetrics.MinWindowWidth));
        Assert.Equal(ToolbarLayoutMetrics.SettingsActionWidth,
            ToolbarLayoutMetrics.ActionsColumn(ToolbarLayoutMetrics.SettingsMinWindow));
        Assert.Equal(ToolbarLayoutMetrics.OverflowActionsColumn,
            ToolbarLayoutMetrics.ActionsColumn(ToolbarLayoutMetrics.OverflowMinWindow));
        Assert.Equal(ToolbarLayoutMetrics.FullActionsColumn,
            ToolbarLayoutMetrics.ActionsColumn(ToolbarLayoutMetrics.FullActionsMinWindow));
    }

    [Theory]
    [InlineData(400, 100)]
    [InlineData(434, 100)]
    [InlineData(465, 100)]
    [InlineData(620, 100)]
    [InlineData(596, 200)]
    [InlineData(760, 200)]
    [InlineData(1920, 200)]
    public void AddressWidth_ShrinksOnlyWhileTheWindowIsNarrow(double window, double expected)
    {
        Assert.Equal(expected, ToolbarLayoutMetrics.AddressWidth(window), 3);
    }

    [Fact]
    public void AddressWidth_NeverLeavesItsBand()
    {
        for (var window = 0.0; window <= 2400; window += 7)
        {
            var width = ToolbarLayoutMetrics.AddressWidth(window);
            Assert.InRange(width, ToolbarLayoutMetrics.AddressMinWidth, ToolbarLayoutMetrics.AddressMaxWidth);
        }
    }

    [Theory]
    [InlineData(400, 434)]
    [InlineData(434, 465)]
    [InlineData(465, 620)]
    [InlineData(620, 2400)]
    public void AddressWidth_IsMonotonicInsideEachLadderStep(double from, double to)
    {
        // The field only ever widens as the window widens, except where the row has just won
        // room for another action button: that button is bought with the field's width, which
        // is the order the ladder spends width in.
        double previous = -1;
        for (var window = from; window < to; window += 1)
        {
            var width = ToolbarLayoutMetrics.AddressWidth(window);
            Assert.True(width >= previous - 0.001, $"the field must not shrink within the step at {window}");
            previous = width;
        }
    }

    [Fact]
    public void AddressWidth_ReachesTheFullField()
    {
        Assert.Equal(ToolbarLayoutMetrics.AddressMaxWidth, ToolbarLayoutMetrics.AddressWidth(2000));
    }

    [Fact]
    public void AddressWidth_WithoutAWindow_FallsBackToTheFloor()
    {
        Assert.Equal(ToolbarLayoutMetrics.AddressMinWidth, ToolbarLayoutMetrics.AddressWidth(0));
        Assert.Equal(ToolbarLayoutMetrics.AddressMinWidth, ToolbarLayoutMetrics.AddressWidth(double.NaN));
        Assert.Equal(ToolbarLayoutMetrics.AddressMinWidth, ToolbarLayoutMetrics.AddressWidth(double.PositiveInfinity));
    }

    [Fact]
    public void MinTabStrip_HoldsTheNewTabButtonAndOneIconCard()
    {
        Assert.Equal(ToolbarLayoutMetrics.NewTabWidth + ToolbarLayoutMetrics.NewTabMargin + TabShrinkPanel.IconTabWidth,
            ToolbarLayoutMetrics.MinTabStrip);
    }

    [Fact]
    public void Row_ClosesAtEveryWidthFromTheMinimumUp()
    {
        var misses = new List<double>();
        for (var window = ToolbarLayoutMetrics.MinWindowWidth; window <= 1200; window += 1)
        {
            var centre = ToolbarLayoutMetrics.CentreWidth(window);
            var needed = ToolbarLayoutMetrics.AddressWidth(window)
                + ToolbarLayoutMetrics.AddressGap
                + ToolbarLayoutMetrics.MinTabStrip;
            if (centre < needed - 0.001) misses.Add(window);
        }
        Assert.Empty(misses);
    }

    [Fact]
    public void ToolbarFits_IsTrueForEveryWidthAtOrAboveTheMinimum()
    {
        var misses = new List<double>();
        for (var window = ToolbarLayoutMetrics.MinWindowWidth; window <= 1200; window += 1)
            if (!ToolbarLayoutMetrics.ToolbarFits(window)) misses.Add(window);
        Assert.Empty(misses);
    }

    [Fact]
    public void ActionCommands_StayOnTheirShortcuts()
    {
        // The row drops these buttons as the window narrows, so the shortcuts are what keeps the
        // commands reachable at the narrowest widths.
        foreach (var (name, combo) in new[]
                 {
                     ("Downloads", "Ctrl+J"),
                     ("ToggleReader", "Ctrl+Shift+R"),
                     ("OpenSettings", "Ctrl+,"),
                 })
        {
            var def = ShortcutDefaults.All.FirstOrDefault(s => s.Name == name);
            Assert.NotNull(def);
            Assert.Equal(combo, def.DefaultCombo);
        }
    }

    [Fact]
    public void MainWindowXaml_LaysTheToolbarOutAsOneRow()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(RepoRoot(), "MainWindow.xaml"));
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        ns.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml");

        var toolbar = doc.SelectSingleNode("//*[@x:Name='Toolbar']", ns);
        Assert.NotNull(toolbar);
        Assert.Equal("38", toolbar.Attributes?["Height"]?.Value);
        // No style block means no width driven height, so the toolbar cannot grow to a second row.
        Assert.Null(toolbar.SelectSingleNode("w:Border.Style", ns));

        var row = doc.SelectSingleNode("//*[@x:Name='ToolbarRow']", ns);
        Assert.NotNull(row);
        var rows = row.SelectNodes("w:Grid.RowDefinitions/w:RowDefinition", ns);
        Assert.NotNull(rows);
        Assert.Single(rows);

        // The middle of the row stays in the middle column, so the address field and the tab
        // strip never move below the navigation buttons.
        var centre = doc.SelectSingleNode("//*[@x:Name='ToolbarCenter']", ns);
        Assert.NotNull(centre);
        Assert.Equal("1", centre.Attributes?["Grid.Column"]?.Value);
        Assert.Null(centre.SelectSingleNode("w:DockPanel.Style", ns));

        // The window minimum is the derived one, not a number typed into the markup.
        var window = doc.SelectSingleNode("//w:Window", ns);
        Assert.NotNull(window);
        Assert.Contains("ToolbarLayoutMetrics.MinWindowWidth", window.Attributes?["MinWidth"]?.Value);

        // The address field is its full width and is capped by the width the row can spare.
        var address = doc.SelectSingleNode("//*[@x:Name='StandardAddressBarBorder']", ns);
        Assert.NotNull(address);
        Assert.Equal("200", address.Attributes?["Width"]?.Value);
        Assert.Contains("AddressFieldMaxWidthConverter", address.Attributes?["MaxWidth"]?.Value);

        // The optional action buttons carry the width gate that folds them into the overflow
        // button, and the settings button carries its own.
        var overflow = doc.SelectSingleNode("//*[@x:Name='ToolbarOverflowButton']", ns);
        Assert.NotNull(overflow);
        Assert.NotNull(overflow.SelectSingleNode("w:Button.ContextMenu", ns));
        var xaml = ReadRoot("MainWindow.xaml");
        Assert.Contains("ToolbarActionVisibilityConverter", xaml);
        Assert.Contains("ConverterParameter=Optional", xaml);
        Assert.Contains("ConverterParameter=Overflow", xaml);
        Assert.Contains("ConverterParameter=Settings", xaml);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stride.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string ReadRoot(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative));
}
