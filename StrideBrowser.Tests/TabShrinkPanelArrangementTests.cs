using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StrideBrowser.Models;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Drives the real panel through real WPF measure and arrange passes. A window resize changes
/// the viewport on one pass and every card retargets on that same pass, so the cards must land
/// on the new bounds there instead of sliding after them; a tab action keeps the viewport, so
/// the slide is still the right behaviour there. The panel is measured and arranged directly,
/// which is the same layout path the ItemsPresenter drives in the running app.
/// </summary>
public sealed class TabShrinkPanelArrangementTests
{
    /// <summary>WPF layout is apartment affine, so the body runs on its own STA thread.</summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>One tab action keeps the viewport, a resize does not.</summary>
    [Fact]
    public void ShouldAnimate_SnapsOnAViewportChangeAndSlidesOnATabAction()
    {
        Assert.False(TabShrinkPanel.ShouldAnimate(viewportChanged: true, hasCurrent: true, dragging: false, animationsEnabled: true),
            "a resize must not start an animation, or the strip chases the pointer");
        Assert.True(TabShrinkPanel.ShouldAnimate(viewportChanged: false, hasCurrent: true, dragging: false, animationsEnabled: true),
            "a tab action with the viewport unchanged must still slide");
        Assert.False(TabShrinkPanel.ShouldAnimate(false, false, false, true), "a card with no current bounds cannot animate");
        Assert.False(TabShrinkPanel.ShouldAnimate(false, true, true, true), "a drag places cards directly");
        Assert.False(TabShrinkPanel.ShouldAnimate(false, true, false, false), "the system animation preference is obeyed");
    }

    [Fact]
    public void Arrange_AfterAViewportChange_LandsEveryCardOnTheNewBoundsAtOnce() => RunSta(() =>
    {
        var panel = new TabShrinkPanel { ViewportWidth = 900 };
        var tabs = Enumerable.Range(0, 6).Select(i => new BrowserTab("https://stride.example/" + i) { IsActive = i == 0 }).ToArray();
        var cards = tabs.Select(tab => (ListBoxItem)Add(panel, tab)).ToArray();

        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));

        // The resize: the viewport and the arrange width both move.
        panel.ViewportWidth = 500;
        panel.Measure(new Size(500, 28));
        panel.Arrange(new Rect(0, 0, 500, 28));

        var expected = TabShrinkPanel.ComputeWidths(500, tabs.Select(t => t.IsPinned).ToArray(), 0);
        var x = 0.0;
        for (var i = 0; i < cards.Length; i++)
        {
            var target = panel.GetTargetBounds(cards[i]);
            var slot = LayoutInformation.GetLayoutSlot(cards[i]);
            Assert.Equal(x, target.X, 3);
            Assert.Equal(expected[i], target.Width, 3);
            Assert.Equal(target.X, slot.X, 3);
            Assert.Equal(target.Width, slot.Width, 3);
            Assert.False(cards[i].HasAnimatedProperties,
                "a card is still animating toward its old bounds after a resize");
            x += expected[i] + TabShrinkPanel.TabGap;
        }
    });

    [Fact]
    public void Arrange_SecondPassAtTheNewViewport_ChangesNothing() => RunSta(() =>
    {
        var panel = new TabShrinkPanel { ViewportWidth = 900 };
        var tabs = Enumerable.Range(0, 4).Select(i => new BrowserTab("https://stride.example/" + i) { IsActive = i == 0 }).ToArray();
        var cards = tabs.Select(tab => (ListBoxItem)Add(panel, tab)).ToArray();

        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));
        panel.ViewportWidth = 520;
        panel.Measure(new Size(520, 28));
        panel.Arrange(new Rect(0, 0, 520, 28));
        var settled = cards.Select(c => LayoutInformation.GetLayoutSlot(c)).ToArray();

        // A repeat pass at the same viewport is not a resize, so nothing moves again and no
        // clock is started on top of the cards that are already in place.
        panel.Measure(new Size(520, 28));
        panel.Arrange(new Rect(0, 0, 520, 28));

        for (var i = 0; i < cards.Length; i++)
        {
            Assert.Equal(settled[i].X, LayoutInformation.GetLayoutSlot(cards[i]).X, 3);
            Assert.Equal(settled[i].Width, LayoutInformation.GetLayoutSlot(cards[i]).Width, 3);
            Assert.False(cards[i].HasAnimatedProperties);
        }
    });

    [Fact]
    public void HoldWidths_AreDroppedWhenTheViewportChanges() => RunSta(() =>
    {
        var panel = new TabShrinkPanel { ViewportWidth = 900 };
        var tabs = Enumerable.Range(0, 6).Select(i => new BrowserTab("https://stride.example/" + i) { IsActive = i == 0 }).ToArray();
        var cards = tabs.Select(tab => (ListBoxItem)Add(panel, tab)).ToArray();

        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));
        var held = panel.GetTargetBounds(cards[1]).Width;
        panel.HoldWidths();

        // Resize with the hold in place. The hold exists to keep the close button under the
        // pointer for one close, not to pin an old width into the new viewport.
        panel.ViewportWidth = 500;
        panel.Measure(new Size(500, 28));
        panel.Arrange(new Rect(0, 0, 500, 28));

        var expected = TabShrinkPanel.ComputeWidths(500, tabs.Select(t => t.IsPinned).ToArray(), 0);
        Assert.NotEqual(held, expected[1], 3);
        for (var i = 0; i < cards.Length; i++)
            Assert.Equal(expected[i], panel.GetTargetBounds(cards[i]).Width, 3);
    });

    [Fact]
    public void HoldWidths_StillPinTheStripWhileTheViewportIsUnchanged() => RunSta(() =>
    {
        var panel = new TabShrinkPanel { ViewportWidth = 900 };
        var tabs = Enumerable.Range(0, 6).Select(i => new BrowserTab("https://stride.example/" + i) { IsActive = i == 0 }).ToArray();
        var cards = tabs.Select(tab => (ListBoxItem)Add(panel, tab)).ToArray();

        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));
        var held = panel.GetTargetBounds(cards[1]).Width;
        panel.HoldWidths();

        // A close removes a tab and keeps the viewport: the held width is what stops the
        // remaining cards from reflowing under the pointer.
        panel.Children.Remove(cards[0]);
        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));

        Assert.Equal(held, panel.GetTargetBounds(cards[1]).Width, 3);
        panel.ReleaseWidths();
        panel.Measure(new Size(900, 28));
        panel.Arrange(new Rect(0, 0, 900, 28));
        Assert.NotEqual(held, panel.GetTargetBounds(cards[1]).Width, 3);
    });

    [Fact]
    public void Arrange_PinnedTab_KeepsTheIconWidth() => RunSta(() =>
    {
        var panel = new TabShrinkPanel { ViewportWidth = 420 };
        var tabs = new[]
        {
            new BrowserTab("https://stride.example/a") { IsPinned = true },
            new BrowserTab("https://stride.example/b") { IsActive = true },
            new BrowserTab("https://stride.example/c"),
        };
        var cards = tabs.Select(tab => (ListBoxItem)Add(panel, tab)).ToArray();

        panel.Measure(new Size(420, 28));
        panel.Arrange(new Rect(0, 0, 420, 28));

        Assert.Equal(TabShrinkPanel.IconTabWidth, panel.GetTargetBounds(cards[0]).Width, 3);
        Assert.Equal(TabShrinkPanel.IconTabWidth, LayoutInformation.GetLayoutSlot(cards[0]).Width, 3);
    });

    private static UIElement Add(TabShrinkPanel panel, BrowserTab tab)
    {
        var item = new ListBoxItem { DataContext = tab, Width = double.NaN };
        panel.Children.Add(item);
        return item;
    }
}
