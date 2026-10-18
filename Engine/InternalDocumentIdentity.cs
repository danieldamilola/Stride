using System;

namespace StrideBrowser.Engine;

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

    public InternalDocumentIdentity(Guid tabId, TabEngine.InternalPageKind pageKind, string documentToken, long intentId)
    {
        TabId = tabId;
        PageKind = pageKind;
        DocumentToken = documentToken;
        IntentId = intentId;
    }
}
