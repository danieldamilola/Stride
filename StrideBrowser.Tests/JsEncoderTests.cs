using StrideBrowser.Helpers;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Locks in the encoding contract for settings values that get embedded
/// directly in HTML attributes and text content. These values originate from
/// user-editable shortcut strings, so a regression in the encoder would be
/// an XSS in the settings page.
/// </summary>
public class JsEncoderTests
{
    [Fact]
    public void HtmlEncode_EscapesAngleBracketsAndQuotes()
    {
        // An ampersand in the input must become &amp; so the encoder is
        // safe to use on text that may contain entities.
        var encoded = JsEncoder.HtmlEncode("<a href=\"x\">a & b</a>");
        Assert.Contains("&lt;", encoded);
        Assert.Contains("&gt;", encoded);
        Assert.Contains("&quot;", encoded);
        Assert.Contains("&amp;", encoded);
        // The raw markup must not survive.
        Assert.DoesNotContain("<a ", encoded);
        Assert.DoesNotContain("</a>", encoded);
    }

    [Fact]
    public void HtmlEncode_PlainString_Unchanged()
    {
        Assert.Equal("hello", JsEncoder.HtmlEncode("hello"));
    }

    [Fact]
    public void HtmlEncode_SingleQuote_BecomesNumericEntity()
    {
        // The settings page renders values inside double-quoted attributes,
        // so the single-quote entity is a defence-in-depth measure, not a
        // hard requirement. The numeric form is used (instead of &apos;)
        // because some older browsers do not recognise the named form.
        Assert.Contains("&#39;", JsEncoder.HtmlEncode("don't"));
    }

    [Theory]
    [InlineData("'", "\\'")]
    [InlineData("\"", "&quot;")]
    [InlineData("\\", "\\\\")]
    [InlineData("\n", "\\n")]
    [InlineData("\r", "\\r")]
    [InlineData("&", "&amp;")]
    public void Encode_EscapesEachDangerousCharacterForJsInAttribute(string input, string expected)
    {
        Assert.Contains(expected, JsEncoder.Encode(input));
    }

    [Fact]
    public void Encode_NormalShortcutCombo_IsUnchanged()
    {
        // The happy path: an ordinary Ctrl+Shift+T must round-trip through
        // the encoder without picking up stray characters.
        Assert.Equal("Ctrl+Shift+T", JsEncoder.Encode("Ctrl+Shift+T"));
    }

    [Fact]
    public void Encode_MaliciousCombo_BreaksOutOfJsLiteral()
    {
        // Adversarial: a value that closes the single-quoted JS literal in
        // an onclick and injects a new attribute must not survive the
        // encoder unescaped.
        var malicious = "x' onmouseover=\"alert(1)\"";
        var encoded = JsEncoder.Encode(malicious);

        // The single quote must be escaped so the attacker cannot close
        // the JS literal that wraps the value in onclick="resetShortcut('...')".
        Assert.Contains("\\'", encoded);

        // The double quote must be HTML-entity-encoded so the surrounding
        // double-quoted attribute is preserved.
        Assert.Contains("&quot;", encoded);
    }
}

