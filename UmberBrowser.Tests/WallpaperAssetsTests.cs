using System.IO;
using System.Windows.Media.Imaging;
using Xunit;

namespace UmberBrowser.Tests;

public sealed class WallpaperAssetsTests
{
    [Fact]
    public void BundledWallpapersAndPickerThumbnailsDecodeWithinTheirBudgets()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "Pages", "Backgrounds");
        var files = Directory.GetFiles(folder, "*.jpg");
        Assert.Equal(14, files.Length);
        long totalBytes = 0;
        foreach (var file in files)
        {
            using var image = File.OpenRead(file);
            var frame = BitmapDecoder.Create(image, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            Assert.InRange(frame.PixelWidth, 1920, 2560);
            Assert.InRange(frame.PixelHeight, 1080, 1440);
            var thumbnail = Path.Combine(folder, "Thumbnails", Path.GetFileName(file));
            using var preview = File.OpenRead(thumbnail);
            var previewFrame = BitmapDecoder.Create(preview, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            Assert.InRange(previewFrame.PixelWidth, 240, 320);
            Assert.InRange(previewFrame.PixelHeight, 130, 180);
            totalBytes += image.Length + preview.Length;
        }
        Assert.InRange(totalBytes, 1, 10 * 1024 * 1024);
    }
}

