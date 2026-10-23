using System.Windows.Input;
using UmberBrowser.Services.Input;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The window tunnels every key press through the shortcut map before the address
/// bar sees it, so any binding that lands on a bare editing key would silently
/// stop the user editing text. These lock the defaults down.
/// </summary>
public sealed class EditingKeyShortcutTests
{
    private static readonly (Key Key, string Name)[] EditingKeys =
    [
        (Key.Back, "Backspace"),
        (Key.Delete, "Delete"),
        (Key.A, "A"),
        (Key.S, "S"),
        (Key.D1, "1"),
        (Key.Space, "Space"),
        (Key.Enter, "Enter"),
    ];

    [Theory]
    [InlineData("Backspace")]
    [InlineData("Delete")]
    [InlineData("A")]
    [InlineData("D1")]
    [InlineData("Space")]
    [InlineData("Enter")]
    public void NoDefaultShortcut_BindsToABareEditingKey(string keyName)
    {
        var offenders = ShortcutDefaults.All
            .Where(d => ShortcutDefaults.GetCombo(d.Name, null).Equals(keyName, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{keyName} is bound to {string.Join(", ", offenders)}, which would swallow the key in the address bar");
    }

    [Fact]
    public void EveryEditingKey_ParsesWithoutModifiers()
    {
        foreach (var (key, name) in EditingKeys)
        {
            var parsed = TryParseComboForTest(name);
            Assert.True(parsed.HasValue, $"{name} should be a parseable key name");
            Assert.Equal(ModifierKeys.None, parsed!.Value.Modifiers);
            Assert.Equal(key, parsed.Value.Key);
        }
    }

    [Fact]
    public void EveryDefaultShortcut_UsesAtLeastOneModifierOrIsNotAnEditingKey()
    {
        foreach (var def in ShortcutDefaults.All)
        {
            var combo = ShortcutDefaults.GetCombo(def.Name, null);
            if (!TryParseComboForTest(combo).HasValue)
            {
                // Unparseable combos are reported by their own tests; skip here.
                continue;
            }
            var parsed = TryParseComboForTest(combo)!.Value;
            if (parsed.Modifiers == ModifierKeys.None)
            {
                Assert.DoesNotContain(EditingKeys, e => e.Key == parsed.Key);
            }
        }
    }

    private static (ModifierKeys Modifiers, Key Key)? TryParseComboForTest(string combo)
    {
        if (!KeyboardShortcutMap.TryParseCombo(combo, out var modifiers, out var key)) return null;
        return (modifiers, key);
    }
}