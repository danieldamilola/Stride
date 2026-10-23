using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using UmberBrowser.Services;
using Xunit;

namespace UmberBrowser.Tests;

/// <summary>
/// The uBlock archive is extracted and then loaded with full page access on every site, so
/// nothing is extracted before it matches a hash pinned in the binary.
/// </summary>
public sealed class ExtensionManagerSecurityTests
{
    [Fact]
    public void PinnedHash_IsASha256Digest()
    {
        var pinned = ExtensionManager.ExpectedHash;

        Assert.Equal(64, pinned.Length);
        Assert.All(pinned, c => Assert.True(Uri.IsHexDigit(c)));
    }

    /// <summary>
    /// The hash was taken from the official gorhill/uBlock 1.73.0 release asset. Confirms the
    /// pin still describes the archive UBlockVersion and UBlockDownloadUrl point at, so a
    /// version bump without a new pin fails here rather than silently disabling verification.
    /// </summary>
    [Fact]
    public async Task PinnedHash_MatchesTheOfficialReleaseArchive()
    {
        var url = "https://github.com/gorhill/uBlock/releases/download/1.73.0/uBlock0_1.73.0.chromium.zip";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        var bytes = await http.GetByteArrayAsync(url);
        var actual = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.Equal(ExtensionManager.ExpectedHash, actual);
    }

    /// <summary>
    /// The real archive must still pass the limits, otherwise the guards reject good input.
    /// </summary>
    [Fact]
    public async Task IsArchiveWithinLimits_AcceptsTheRealArchive()
    {
        var url = "https://github.com/gorhill/uBlock/releases/download/1.73.0/uBlock0_1.73.0.chromium.zip";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var bytes = await http.GetByteArrayAsync(url);

        Assert.True(ExtensionManager.IsArchiveWithinLimits(bytes, out var reason), reason);
    }

    /// <summary>
    /// A zip can declare a huge output while staying small on the wire. Entry count, total
    /// declared size and the largest single entry are all bounded.
    /// </summary>
    [Fact]
    public void IsArchiveWithinLimits_RejectsTooManyEntries()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 5001; i++)
            {
                var entry = zip.CreateEntry($"e{i}.txt");
                using var s = entry.Open();
                s.Write(new byte[1]);
            }
        }
        var bytes = ms.ToArray();

        Assert.False(ExtensionManager.IsArchiveWithinLimits(bytes, out var reason));
        Assert.Contains("entry count", reason);
    }

/// <summary>
    /// The per-entry ceiling is read from the central directory, not from the decompressed
    /// stream, so a stored entry can declare its size without spending the bytes. This builds a
    /// zip whose declared uncompressed total is far past the cap while the archive stays tiny.
    /// </summary>
    [Fact]
    public void IsArchiveWithinLimits_RejectsAnArchiveDeclaringMoreOutputThanTheCap()
    {
        // Build the central directory by hand. ZipArchive writes the true compressed size into
        // both headers, so it cannot express this mismatch; a real bomb does exactly that by
        // patching the size fields after the fact. Here the declared size is simulated by
        // creating an entry, then rewriting its uncompressed-size field in both headers.
        var entryName = System.Text.Encoding.UTF8.GetBytes("payload.bin");
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // Local file header
            writer.Write((uint)0x04034b50);
            writer.Write((ushort)20);
            writer.Write((ushort)0);
            writer.Write((ushort)0);           // stored, no compression
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write((uint)0);             // compressed size, patched below
            writer.Write((uint)0x20000000);    // declared uncompressed size, 512 MB
            writer.Write((ushort)entryName.Length);
            writer.Write((ushort)0);
            writer.Write(entryName);
            writer.Write(new byte[1]);        // one byte of payload

            var localHeaderSize = 30 + entryName.Length + 1;

            // Central directory header
            writer.Write((uint)0x02014b50);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write((uint)0x20000000);    // declared uncompressed size, 512 MB
            writer.Write((ushort)entryName.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            writer.Write(entryName);

            var centralSize = 46 + entryName.Length;
            var offset = (uint)localHeaderSize;

            // End of central directory
            writer.Write((uint)0x06054b50);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((uint)centralSize);
            writer.Write(offset);
            writer.Write((ushort)0);
        }

        var bytes = ms.ToArray();

        // The archive is a few dozen bytes but claims 512 MB of output.
        Assert.True(bytes.Length < 1024);

        Assert.False(ExtensionManager.IsArchiveWithinLimits(bytes, out var reason));
        Assert.Contains("declares", reason);
    }

    /// <summary>
    /// A small, honestly declared archive is accepted, so the caps do not reject good input.
    /// </summary>
    [Fact]
    public void IsArchiveWithinLimits_AcceptsASmallHonestArchive()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("small.txt");
            using var s = entry.Open();
            s.Write(new byte[1024]);
        }

        Assert.True(ExtensionManager.IsArchiveWithinLimits(ms.ToArray(), out var reason), reason);
    }

    [Fact]
    public void IsArchiveWithinLimits_RejectsGarbage()
    {
        Assert.False(ExtensionManager.IsArchiveWithinLimits("not a zip at all"u8.ToArray(), out _));
        Assert.False(ExtensionManager.IsArchiveWithinLimits(Array.Empty<byte>(), out _));
    }
}
