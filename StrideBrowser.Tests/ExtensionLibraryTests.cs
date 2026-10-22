using System.IO;
using System.Text.Json;
using StrideBrowser.Services.Extensions;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stride-extension-library-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "nested", "extension-library.json");

    public ExtensionLibraryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void NewLibrary_LeavesDeveloperModeOffWithoutCreatingAFile()
    {
        var library = new ExtensionLibrary(FilePath);

        Assert.False(library.DeveloperMode);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task SetDeveloperMode_PersistsBothEnabledAndDisabledAcrossInstances()
    {
        var library = new ExtensionLibrary(FilePath);
        await library.SetDeveloperModeAsync(true);

        Assert.True(library.DeveloperMode);
        Assert.True(new ExtensionLibrary(FilePath).DeveloperMode);

        await library.SetDeveloperModeAsync(false);

        Assert.False(library.DeveloperMode);
        Assert.False(new ExtensionLibrary(FilePath).DeveloperMode);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task SetDeveloperMode_KeepsSavedExtensionSourcesAndPins()
    {
        var sources = new Dictionary<string, string> { ["extension-one"] = Path.Combine(_root, "unpacked extension") };
        var pins = new HashSet<string> { "extension-one", "extension-two" };
        WritePreferences(new ExtensionLibrary.Preferences { Sources = sources, Pinned = pins });
        var library = new ExtensionLibrary(FilePath);

        await library.SetDeveloperModeAsync(true);

        var saved = ReadPreferences();
        Assert.True(saved.DeveloperMode);
        Assert.Equal(sources["extension-one"], Assert.Single(saved.Sources).Value);
        Assert.True(pins.SetEquals(saved.Pinned));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"developerMode\":\"yes\"}")]
    public async Task BrokenPreferences_FallBackWithoutBlockingALaterSave(string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, contents);
        var library = new ExtensionLibrary(FilePath);

        Assert.False(library.DeveloperMode);
        await library.SetDeveloperModeAsync(true);

        Assert.True(new ExtensionLibrary(FilePath).DeveloperMode);
        Assert.Empty(ReadPreferences().Sources);
        Assert.Empty(ReadPreferences().Pinned);
    }

    [Fact]
    public async Task NullSavedCollections_AreNormalizedWhileRestoringDeveloperMode()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{"developerMode":true,"sources":null,"pinned":null}""");
        var library = new ExtensionLibrary(FilePath);

        Assert.True(library.DeveloperMode);
        await library.SetDeveloperModeAsync(false);

        Assert.Empty(ReadPreferences().Sources);
        Assert.Empty(ReadPreferences().Pinned);
        Assert.False(new ExtensionLibrary(FilePath).DeveloperMode);
    }

    [Fact]
    public async Task FailedReplacement_DoesNotChangeMemoryOrExistingSavedPreferencesAndAllowsRetry()
    {
        WritePreferences(new ExtensionLibrary.Preferences
        {
            DeveloperMode = true,
            Sources = new() { ["extension-one"] = Path.Combine(_root, "unpacked") },
            Pinned = new() { "extension-one" }
        });
        var original = File.ReadAllBytes(FilePath);
        var library = new ExtensionLibrary(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => library.SetDeveloperModeAsync(false));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.True(library.DeveloperMode);
        }

        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.True(new ExtensionLibrary(FilePath).DeveloperMode);
        Assert.Single(ReadPreferences().Sources);
        Assert.Contains("extension-one", ReadPreferences().Pinned);

        await library.SetDeveloperModeAsync(false);

        Assert.False(library.DeveloperMode);
        Assert.False(new ExtensionLibrary(FilePath).DeveloperMode);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Single(ReadPreferences().Sources);
        Assert.Contains("extension-one", ReadPreferences().Pinned);
    }

    [Fact]
    public async Task FailedFirstSave_DoesNotEnableDeveloperModeOrCreatePreferences()
    {
        var parent = Path.GetDirectoryName(FilePath)!;
        File.WriteAllText(parent, "This file blocks creating the preferences directory.");
        var library = new ExtensionLibrary(FilePath);

        await Assert.ThrowsAsync<IOException>(() => library.SetDeveloperModeAsync(true));

        Assert.False(library.DeveloperMode);
        Assert.False(File.Exists(FilePath));
        Assert.False(new ExtensionLibrary(FilePath).DeveloperMode);
    }

    [Fact]
    public async Task SuccessfulSave_NotifiesSubscribersAfterTheValueIsPersisted()
    {
        var library = new ExtensionLibrary(FilePath);
        var observed = new List<(bool Memory, bool Disk)>();
        library.Changed += () => observed.Add((library.DeveloperMode, new ExtensionLibrary(FilePath).DeveloperMode));

        await library.SetDeveloperModeAsync(true);
        await library.SetDeveloperModeAsync(false);

        Assert.Equal(new[] { (true, true), (false, false) }, observed);
    }

    [Fact]
    public async Task DeveloperModeOff_RejectsLoadAndReloadBeforeAccessingTheNativeProfile()
    {
        var library = new ExtensionLibrary(FilePath);

        var loadError = await Assert.ThrowsAsync<InvalidOperationException>(() => library.LoadAsync(null!, Path.Combine(_root, "not-an-extension")));
        var reloadError = await Assert.ThrowsAsync<InvalidOperationException>(() => library.ReloadAsync(null!, "extension-one"));

        Assert.Contains("Turn on Developer mode", loadError.Message);
        Assert.Contains("Turn on Developer mode", reloadError.Message);
        Assert.False(File.Exists(FilePath));
    }

    private void WritePreferences(ExtensionLibrary.Preferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(preferences, ExtensionLibrary.JsonOptions));
    }

    private ExtensionLibrary.Preferences ReadPreferences() => JsonSerializer.Deserialize<ExtensionLibrary.Preferences>(File.ReadAllText(FilePath), ExtensionLibrary.JsonOptions)!;

    public void Dispose() => Directory.Delete(_root, true);
}
