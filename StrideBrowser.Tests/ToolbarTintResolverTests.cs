using System.Windows.Media;
using StrideBrowser.Services.UI;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ToolbarTintResolverTests
{
    private static readonly Color Base = Color.FromRgb(0x1A, 0x1A, 0x2E);

    [Fact]
    public void ResolveTargetColor_EmptyHex_ReturnsBase()
    {
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor("", Base));
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor(null, Base));
    }

    [Fact]
    public void ResolveTargetColor_ValidHex_ReturnsColor()
    {
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56),
            ToolbarTintResolver.ResolveTargetColor("#123456", Base));
    }

    [Fact]
    public void ResolveTargetColor_WhiteHex_FallsBackToDark()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x11, 0x11),
            ToolbarTintResolver.ResolveTargetColor("#FFFFFF", Base));
    }

    [Fact]
    public void ResolveTargetColor_StrongGreen_FallsBackToDark()
    {
        Assert.Equal(Color.FromRgb(0x11, 0x11, 0x11),
            ToolbarTintResolver.ResolveTargetColor("#00C853", Base));
    }

    [Fact]
    public void ResolveTargetColor_BadHex_ReturnsBase()
    {
        Assert.Equal(Base, ToolbarTintResolver.ResolveTargetColor("not-a-color", Base));
    }

    [Fact]
    public void IsLight_ClassifiesCorrectly()
    {
        Assert.True(ToolbarTintResolver.IsLight(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        Assert.False(ToolbarTintResolver.IsLight(Color.FromRgb(0x11, 0x11, 0x11)));
    }
}
