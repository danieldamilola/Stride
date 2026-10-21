using System.IO;
using System.Text;
using System.Text.Json;
using StrideBrowser.Services.Extensions;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class ExtensionProfileReaderTests : IDisposable
{
    private const string ClipboardId = "dgiklkfkllikcanfonkcabmbdfmgleag";
    private const string PdfId = "mhjfbmdgcfjbbpaeojofohoefgiehjai";
    private const string CustomId = "abcdefghijklmnopabcdefghijklmnop";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stride-extension-profile-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "Default");
    private string PreferencesFile => Path.Combine(Profile, "Secure Preferences");

    public ExtensionProfileReaderTests() => Directory.CreateDirectory(Profile);

    [Fact]
    public void Read_ReturnsRuntimeComponentsAndUnpackedSourceFolders()
    {
        var clipboard = Folder("runtime/clipboard");
        var pdf = Folder("runtime/pdf");
        var custom = Folder("unpacked extension");
        WriteSettings(new Dictionary<string, object>
        {
            [ClipboardId] = new { location = 5, path = clipboard },
            [PdfId] = new { location = 5, path = pdf },
            [CustomId] = new { location = 4, path = custom }
        });
        var original = File.ReadAllBytes(PreferencesFile);

        var entries = ExtensionProfileReader.Read(Profile);

        Assert.Equal(3, entries.Count);
        Assert.Equal(clipboard, entries.Single(x => x.Id == ClipboardId).Folder);
        Assert.True(entries.Single(x => x.Id == ClipboardId).IsComponent);
        Assert.True(entries.Single(x => x.Id == PdfId).IsComponent);
        Assert.False(entries.Single(x => x.Id == CustomId).IsComponent);
        Assert.Equal(custom, entries.Single(x => x.Id == CustomId).Folder);
        Assert.Equal(original, File.ReadAllBytes(PreferencesFile));
        Assert.Equal(new[] { PreferencesFile }, Directory.GetFiles(Profile));
    }

    [Fact]
    public void Read_MissingPreferencesReturnsEmptyWithoutCreatingFiles()
    {
        Assert.Empty(ExtensionProfileReader.Read(Profile));
        Assert.Empty(Directory.GetFileSystemEntries(Profile));
        var missingProfile = Path.Combine(_root, "missing");
        Assert.Empty(ExtensionProfileReader.Read(missingProfile));
        Assert.False(Directory.Exists(missingProfile));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"extensions\":[]}")]
    [InlineData("{\"extensions\":{\"settings\":[]}}")]
    public void Read_MalformedOrMissingExtensionSettingsReturnsEmpty(string json)
    {
        File.WriteAllText(PreferencesFile, json);
        Assert.Empty(ExtensionProfileReader.Read(Profile));
    }

    [Fact]
    public void Read_SkipsMalformedEntriesAndKeepsTheValidEntry()
    {
        var folder = Folder("valid");
        WriteSettings(new Dictionary<string, object>
        {
            [CustomId] = new { location = 4, path = folder },
            [new string('a', 31)] = new { location = 5, path = folder },
            [new string('q', 32)] = new { location = 5, path = folder },
            [new string('A', 32)] = new { location = 5, path = folder },
            [new string('b', 32)] = new { location = "5", path = folder },
            [new string('c', 32)] = new { location = -1, path = folder },
            [new string('d', 32)] = new { location = 4, path = 42 },
            [new string('e', 32)] = new { location = 5 },
            [new string('f', 32)] = new[] { folder }
        });

        Assert.Equal(CustomId, Assert.Single(ExtensionProfileReader.Read(Profile)).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/extension")]
    [InlineData("C:extension")]
    [InlineData("C:\\")]
    [InlineData("C:/")]
    [InlineData("\\\\server\\share\\extension")]
    [InlineData("\\\\?\\C:\\extension")]
    [InlineData("https://example.com/extension")]
    [InlineData("C:\\extension:stream")]
    [InlineData("C:\\extension\\*.json")]
    [InlineData("C:\\extension\\bad\nname")]
    public void Read_RejectsNonlocalOrInvalidFolderPaths(string path)
    {
        WriteSettings(new Dictionary<string, object> { [CustomId] = new { location = 4, path } });
        Assert.Empty(ExtensionProfileReader.Read(Profile));
    }

    [Fact]
    public void Read_RejectsMissingFoldersAndFilesUsedAsFolders()
    {
        var file = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(file, "content");
        WriteSettings(new Dictionary<string, object>
        {
            [CustomId] = new { location = 4, path = file },
            [ClipboardId] = new { location = 5, path = Path.Combine(_root, "not-found") }
        });
        Assert.Empty(ExtensionProfileReader.Read(Profile));
    }

    [Fact]
    public void Read_RejectsOversizedFile()
    {
        using (var file = File.Create(PreferencesFile)) file.SetLength(16 * 1024 * 1024 + 1);
        Assert.Empty(ExtensionProfileReader.Read(Profile));
    }

    [Fact]
    public void Read_RejectsExcessiveJsonDepth()
    {
        File.WriteAllText(PreferencesFile, new string('[', 65) + "0" + new string(']', 65));
        Assert.Empty(ExtensionProfileReader.Read(Profile));
    }

    [Fact]
    public void Read_AcceptsUtf8BomAndReadsWhileTheBrowserHoldsASharedHandle()
    {
        var folder = Folder("extension");
        WriteSettings(new Dictionary<string, object> { [CustomId] = new { location = 4, path = folder + Path.DirectorySeparatorChar } });
        var bytes = File.ReadAllBytes(PreferencesFile);
        File.WriteAllBytes(PreferencesFile, Encoding.UTF8.GetPreamble().Concat(bytes).ToArray());
        using var browser = new FileStream(PreferencesFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(folder, Assert.Single(ExtensionProfileReader.Read(Profile)).Folder);
    }

    private string Folder(string relative)
    {
        var folder = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(folder);
        return folder;
    }
    private void WriteSettings(Dictionary<string, object> settings) => File.WriteAllText(PreferencesFile, JsonSerializer.Serialize(new { extensions = new { settings } }));
    public void Dispose() => Directory.Delete(_root, true);
}
