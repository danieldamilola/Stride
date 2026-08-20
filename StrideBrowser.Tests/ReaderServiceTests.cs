using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Scaffold for IReaderService session lifecycle tests.
/// Real service stores per-tab ReaderSession, writes only via tabId key,
/// raises SessionChanged, and is observed by a single shared ReaderViewModel that re-derives on ActiveTabChanged.
/// Tests here will mock IReaderExtractor and WebMessageRouter path.
/// </summary>
public sealed class ReaderServiceTests
{
    [Fact(Skip = "service not implemented - scaffold")]
    public void GetSession_ReturnsNull_ForUnknownTab()
    {
    }

    [Fact(Skip = "service not implemented - scaffold")]
    public void RemoveSession_DropsEntry_AndRaisesSessionChanged()
    {
    }

    [Fact(Skip = "service not implemented - scaffold")]
    public void EnterWritesOnlyForCallingTabId_NotActiveTab()
    {
        // Verifies tab-switch-mid-extraction race fix: slow extraction for background tab updates that tab only.
    }
}
