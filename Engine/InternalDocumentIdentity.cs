using System;

namespace UmberBrowser.Engine;

/// <summary>
/// Tracks the active internal document identity for a browser tab.
/// A tab visiting external content has no active internal document identity.
/// </summary>
public sealed class InternalDocumentIdentity
{
    public Guid TabId { get; }
    public TabEngine.InternalPageKind PageKind { get; }
    public string DocumentToken { get; }
    public long IntentId { get; }
    private string? _expectedNavigation;

    public bool TryStartNavigation(string uri)
    {
        var expected = _expectedNavigation;
        _expectedNavigation = null;
        return expected is not null && string.Equals(uri, expected, StringComparison.Ordinal);
    }

    public InternalDocumentIdentity(Guid tabId, TabEngine.InternalPageKind pageKind, string documentToken, long intentId, string? html = null)
    {
        TabId = tabId;
        PageKind = pageKind;
        DocumentToken = documentToken;
        IntentId = intentId;
        _expectedNavigation = html is null ? null : "data:text/html;charset=utf-8;base64," +
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html));
    }
}
