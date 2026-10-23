using System.Diagnostics;
using UmberBrowser.Services.Reader;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class ReaderSanitizerBudgetTests
{
    private readonly ReaderSanitizer _sanitizer = new();
    private const string Origin = "https://example.com/article";

    [Theory]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("iframe")]
    [InlineData("template")]
    public void RepeatedUnclosedDropTagsDiscardTheirContents(string tag)
    {
        var malformed = "<p>before</p>" + string.Concat(Enumerable.Repeat($"<{tag}>", 20_000)) + "hidden";
        var watch = Stopwatch.StartNew();
        Assert.Equal("<p>before</p>", _sanitizer.Sanitize(malformed, Origin));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NestedDropTagsDoNotReleaseInnerMarkup()
    {
        Assert.Equal("<p>before</p><p>after</p>", _sanitizer.Sanitize(
            "<p>before</p><template><template><img src='/secret'></template>hidden</template><p>after</p>", Origin));
    }

    [Theory]
    [InlineData("<p title='unterminated><img src=evil>")]
    [InlineData("<script")]
    [InlineData("<!--<img src=evil>")]
    public void UnfinishedMarkupFailsClosed(string malformed)
    {
        Assert.Equal("<p>safe</p>", _sanitizer.Sanitize("<p>safe</p>" + malformed, Origin));
    }

    [Fact]
    public void QuotedAnglesRemainEscapedAndCannotCreateTags()
    {
        var result = _sanitizer.Sanitize("<p title='a > b <img onerror=evil>'>ok</p>", Origin);
        Assert.Equal("<p title=\"a &gt; b &lt;img onerror=evil&gt;\">ok</p>", result);
    }

    [Fact]
    public void EntityEncodedPrivateAddressAndScriptSchemeAreRejected()
    {
        var result = _sanitizer.Sanitize("<img src='http://127&#46;0&#46;0&#46;1/x'><a href='java&#115;cript:alert(1)'>x</a>", Origin);
        Assert.Equal("<img><a>x</a>", result);
    }

    [Fact]
    public void LargeNormalArticleKeepsItsTextAndAllowedMarkup()
    {
        var article = string.Concat(Enumerable.Repeat("<p>Some article text &amp; context <strong>bold</strong>.</p>", 15_000));
        Assert.Equal(article, _sanitizer.Sanitize(article, Origin));
    }

    [Fact]
    public void OversizedInputThrowsWithoutReturningAnyInput()
    {
        Assert.Throws<InvalidOperationException>(() => _sanitizer.Sanitize(new string('x', ReaderSanitizer.MaxInputCharacters + 1), Origin));
    }

    [Fact]
    public void RepeatedMalformedAttributePrefixesHaveBoundedWork()
    {
        var html = "<p " + string.Concat(Enumerable.Repeat("a=\"\" ", 30_000)) + "title='kept'>text</p>";
        Assert.Equal("<p title=\"kept\">text</p>", _sanitizer.Sanitize(html, Origin));
    }
}

