using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using UmberBrowser.Models;

namespace UmberBrowser.Services.Downloads;

public sealed class DownloadFileInfoService
{
    private sealed record FileDetails(bool Exists, BitmapSource? Icon, string DataUri);
    private readonly Dictionary<string, (DateTime Expires, Task<FileDetails> Task, bool Completed)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record ShellIcon(BitmapSource? Bitmap, string DataUri);
    private readonly ConcurrentDictionary<string, Lazy<ShellIcon>> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _workers = new(3);
    private readonly object _gate = new();

    public async Task RefreshAsync(IEnumerable<DownloadItem> items)
    {
        await Task.WhenAll(items.Select(async item =>
        {
            var path = item.FilePath;
            var completed = item.State == DownloadState.Completed;
            var details = await GetAsync(path, completed);
            if (item.FilePath != path || item.State == DownloadState.Completed && !completed) return;
            item.FileIconDataUri = details.DataUri;
            item.FileIcon = details.Icon;
            if (item.State == DownloadState.Completed) item.IsMissing = !details.Exists;
        }));
    }

    private Task<FileDetails> GetAsync(string path, bool completed)
    {
        if (!IsLocalPath(path)) return Task.FromResult(new FileDetails(false, null, ""));
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_cache.TryGetValue(path, out var cached) && (cached.Completed || !completed) && (cached.Expires > now || !cached.Task.IsCompleted)) return cached.Task;
            if (_cache.Count >= 512)
                foreach (var key in _cache.OrderBy(pair => pair.Value.Expires).Take(64).Select(pair => pair.Key).ToArray()) _cache.Remove(key);
            var task = InspectAsync(path);
            _cache[path] = (now.AddSeconds(5), task, completed);
            return task;
        }
    }

    public static bool IsLocalPath(string path) => !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) &&
        path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private async Task<FileDetails> InspectAsync(string path)
    {
        await _workers.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(() => Inspect(path)).ConfigureAwait(false); }
        finally { _workers.Release(); }
    }

    private FileDetails Inspect(string path)
    {
        var exists = File.Exists(path);
        try
        {
            var perFile = exists && Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico";
            var key = perFile ? path + "|" + File.GetLastWriteTimeUtc(path).Ticks : Path.GetExtension(path);
            if (_icons.Count >= 256) _icons.Clear();
            var icon = _icons.GetOrAdd(key, _ => new Lazy<ShellIcon>(() => ReadIcon(path, perFile))).Value;
            return new(exists, icon.Bitmap, icon.DataUri);
        }
        catch (Exception ex) { Trace.WriteLine($"Download icon lookup failed: {ex.Message}"); return new(exists, null, ""); }
    }

    private static ShellIcon ReadIcon(string path, bool perFile)
    {
        var info = new ShellFileInfo();
        var com = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            // Existing executables use their own resources. Other types use their
            // Windows association, including records whose file was removed.
            const uint icon = 0x100;
            const uint useFileAttributes = 0x10;
            if (SHGetFileInfo(path, 0x80, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), icon | (perFile ? 0u : useFileAttributes)) == IntPtr.Zero || info.Icon == IntPtr.Zero)
                return new(null, "");
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new(bitmap, "data:image/png;base64," + Convert.ToBase64String(output.ToArray()));
        }
        finally
        {
            if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon);
            if (com >= 0) CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
