using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// Scaffold for ReaderViewModel mirroring tests.
/// Real VM is single shared instance that re-reads GetSession(ActiveTabId) on SessionChanged and ActiveTabChanged.
/// </summary>
public sealed class ReaderViewModelTests
{
    [Fact(Skip = "viewmodel not implemented - scaffold")]
    public void OnActiveTabChanged_ResyncsIsInReaderAndCurrent()
    {
    }

    [Fact(Skip = "viewmodel not implemented - scaffold")]
    public void SessionChanged_ForBackgroundTab_DoesNotUpdateVm()
    {
    }

    [Fact(Skip = "viewmodel not implemented - scaffold")]
    public void Toggle_DelegatesToService_ForActiveTab()
    {
    }
}
