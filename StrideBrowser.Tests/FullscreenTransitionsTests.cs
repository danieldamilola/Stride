using System.Windows;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Covers the pure fullscreen state machine that backs both the F11 toggle and
/// WebView2 video fullscreen events.
/// </summary>
public class FullscreenTransitionsTests
{
    [Fact]
    public void Enter_FromWindowed_SavesWindowStateAndActivates()
    {
        var state = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Normal);

        Assert.True(state.IsActive);
        Assert.Equal(WindowState.Normal, state.SavedWindowState);
    }

    [Fact]
    public void Enter_FromMaximized_SavesMaximized()
    {
        var state = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Maximized);

        Assert.True(state.IsActive);
        Assert.Equal(WindowState.Maximized, state.SavedWindowState);
    }

    [Fact]
    public void Enter_WhenAlreadyActive_IsNoOpAndKeepsFirstSavedState()
    {
        var first = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Normal);
        var second = FullscreenTransitions.Enter(first, WindowState.Maximized);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Exit_RestoresSavedWindowStateAndDeactivates()
    {
        var entered = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Maximized);

        var exited = FullscreenTransitions.Exit(entered);

        Assert.False(exited.IsActive);
        Assert.Equal(WindowState.Maximized, exited.SavedWindowState);
    }

    [Fact]
    public void Exit_WhenNotActive_IsNoOp()
    {
        var state = FullscreenTransitions.Exit(FullscreenState.Initial);

        Assert.Equal(FullscreenState.Initial, state);
    }

    [Fact]
    public void Toggle_FromInactive_EntersAndSavesWindowState()
    {
        var state = FullscreenTransitions.Toggle(FullscreenState.Initial, WindowState.Normal);

        Assert.True(state.IsActive);
        Assert.Equal(WindowState.Normal, state.SavedWindowState);
    }

    [Fact]
    public void Toggle_FromActive_ExitsAndPreservesSavedWindowState()
    {
        var entered = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Maximized);

        var toggled = FullscreenTransitions.Toggle(entered, WindowState.Maximized);

        Assert.False(toggled.IsActive);
        Assert.Equal(WindowState.Maximized, toggled.SavedWindowState);
    }

    [Fact]
    public void Toggle_Twice_RoundTripsBackToInitialActiveFlag()
    {
        var state = FullscreenTransitions.Toggle(
            FullscreenTransitions.Toggle(FullscreenState.Initial, WindowState.Normal),
            WindowState.Normal);

        Assert.False(state.IsActive);
    }

    [Fact]
    public void F11WhileVideoFullscreen_ExitsCleanlyAndKeepsSavedState()
    {
        // Simulates the old desync bug: video fullscreen activates via Enter,
        // then the user presses F11, which toggles. The state machine must exit
        // once and keep the original window state for the eventual restore.
        var video = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Normal);
        var afterF11 = FullscreenTransitions.Toggle(video, WindowState.Normal);

        Assert.False(afterF11.IsActive);
        Assert.Equal(WindowState.Normal, afterF11.SavedWindowState);
    }

    [Fact]
    public void DuplicateVideoEvents_StickToSingleTransition()
    {
        // ContainsFullScreenElementChanged can fire per tab; repeated Enter
        // calls must not overwrite the saved window state.
        var once = FullscreenTransitions.Enter(FullscreenState.Initial, WindowState.Normal);
        var thrice = FullscreenTransitions.Enter(FullscreenTransitions.Enter(once, WindowState.Maximized), WindowState.Minimized);

        Assert.True(thrice.IsActive);
        Assert.Equal(WindowState.Normal, thrice.SavedWindowState);
    }
}