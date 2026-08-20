using System.Windows.Input;
using StrideBrowser.Services.Input;
using Xunit;

namespace StrideBrowser.Tests;

public class KeyboardShortcutMapTests
{
    [Theory]
    [InlineData("Ctrl+T", ModifierKeys.Control, Key.T)]
    [InlineData("Ctrl+Shift+T", ModifierKeys.Control | ModifierKeys.Shift, Key.T)]
    [InlineData("Alt+F4", ModifierKeys.Alt, Key.F4)]
    [InlineData("F11", ModifierKeys.None, Key.F11)]
    [InlineData("Ctrl+=", ModifierKeys.Control, Key.OemPlus)]
    [InlineData("Ctrl+-", ModifierKeys.Control, Key.OemMinus)]
    public void TryParseCombo_ParsesExpectedModifiersAndKey(string combo, ModifierKeys expectedMods, Key expectedKey)
    {
        var success = KeyboardShortcutMap.TryParseCombo(combo, out var mods, out var key);

        Assert.True(success);
        Assert.Equal(expectedMods, mods);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    public void TryParseCombo_InvalidInput_ReturnsFalse(string combo)
    {
        Assert.False(KeyboardShortcutMap.TryParseCombo(combo, out _, out _));
    }

    [Fact]
    public void ToComboString_RoundTripsWithTryParseCombo()
    {
        const string original = "Ctrl+Shift+T";
        KeyboardShortcutMap.TryParseCombo(original, out var mods, out var key);

        Assert.Equal(original, KeyboardShortcutMap.ToComboString(mods, key));
    }
}
