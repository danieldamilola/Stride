using System;
using StrideBrowser.ViewModels;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionPanelIconTests
{
    [Fact]
    public void IconBytes_ReturnsPayloadAfterComma()
    {
        var payload = new byte[] { 1, 2, 3 };
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(payload);

        Assert.Equal(payload, ExtensionsPanelViewModel.IconBytes(dataUri));
    }

    [Theory]
    [InlineData("")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png;base64,")]
    public void IconBytes_RejectsDataUriWithoutPayload(string dataUri)
    {
        Assert.Throws<FormatException>(() => ExtensionsPanelViewModel.IconBytes(dataUri));
    }

    [Fact]
    public void IconBytes_RejectsInvalidBase64()
    {
        Assert.Throws<FormatException>(() => ExtensionsPanelViewModel.IconBytes("data:image/png;base64,not base64"));
    }
}
