using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;
using Xunit;

namespace StrideBrowser.Tests;

public sealed class DownloadHistoryTests
{
    [Theory]
    [InlineData(@"C:\Downloads\file.zip", true)]
    [InlineData(@"\\server\share\file.exe", false)]
    [InlineData(@"\\?\C:\Downloads\file.exe", false)]
    [InlineData("relative.zip", false)]
    [InlineData("https://example.com/file.exe", false)]
    [InlineData("", false)]
    public void ShellLookupAcceptsOnlyLocalDrivePaths(string path, bool allowed) =>
        Assert.Equal(allowed, DownloadFileInfoService.IsLocalPath(path));

    [Fact]
    public void PageSnapshotHasRuntimePresentationButDiskRecordsDoNot()
    {
        var item = new DownloadItem { State = DownloadState.Completed, IsMissing = true };
        using var page = JsonDocument.Parse(DownloadJson.SerializeForPage(new[] { item }));
        Assert.True(page.RootElement[0].GetProperty("isMissing").GetBoolean());
        Assert.Equal("", page.RootElement[0].GetProperty("fileIcon").GetString());
        using var disk = JsonDocument.Parse(JsonSerializer.Serialize(item, DownloadJson.Options));
        Assert.False(disk.RootElement.TryGetProperty("isMissing", out _));
        Assert.False(disk.RootElement.TryGetProperty("fileIcon", out _));
        Assert.False(disk.RootElement.TryGetProperty("fileIconDataUri", out _));
    }

    [Fact]
    public void RemoveHistoryPreservesFilesAndDoesNotRemoveActiveTransfers()
    {
        var path = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore();
            var done = new DownloadItem { State = DownloadState.Completed, FilePath = path };
            var active = new DownloadItem();
            store.Add(done); store.Add(active);
            var actions = new DownloadCoordinator(store);
            actions.Remove(active.Id);
            Assert.Contains(active, store.Items);
            actions.Remove(done.Id);
            Assert.DoesNotContain(done, store.Items);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CompletionDoesNotReuseAnEarlierMissingFileSample()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var item = new DownloadItem { FilePath = path };
            var files = new DownloadFileInfoService();
            await files.RefreshAsync(new[] { item });
            await File.WriteAllTextAsync(path, "downloaded content");
            item.State = DownloadState.Completed;
            await files.RefreshAsync(new[] { item });
            Assert.False(item.IsMissing);
            Assert.True(item.CanOpen);
            Assert.NotNull(item.FileIcon);
            Assert.True(item.FileIcon!.IsFrozen);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingCompletedFileUsesAssociationIconAndDisablesOpen()
    {
        var item = new DownloadItem { FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"), State = DownloadState.Completed };
        await new DownloadFileInfoService().RefreshAsync(new[] { item });
        Assert.True(item.IsMissing);
        Assert.False(item.CanOpen);
        Assert.NotNull(item.FileIcon);
    }

    [Theory]
    [InlineData("download-copy:item")]
    [InlineData("download-remove:item")]
    public void FileActionsAreRestrictedToTheDownloadsPage(string route)
    {
        Assert.True(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.Downloads, route));
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.Settings, route));
        Assert.False(IpcSecurityPolicy.IsRouteAllowedForPage(TabEngine.InternalPageKind.NewTab, route));
    }

    private sealed class MemoryStore : IDownloadStore
    {
        public ObservableCollection<DownloadItem> Items { get; } = new();
        public void Add(DownloadItem item) => Items.Add(item);
        public DownloadItem? Get(string id) => Items.FirstOrDefault(item => item.Id == id);
        public void Remove(string id) { if (Get(id) is { } item) Items.Remove(item); }
        public void ClearCompleted() { }
        public void ClearAll() => Items.Clear();
    }
}
