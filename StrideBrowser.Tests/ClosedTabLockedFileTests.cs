using System;
using System.IO;
using System.Linq;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// A history file held open by something else, which is what a failed write looks
/// like on a real machine: the write is refused, and the read that would follow it
/// is refused too.
///
/// Rolling back by reading the file back was the trap. The read came back empty
/// under the lock, so closures that were known to be on disk were dropped from
/// memory, and the store marked the file unreadable, which refuses every later
/// write for the rest of the run. A lock is transient. Once it is released the
/// closures that were on disk are still there, and the ones that were not are still
/// worth reopening.
///
/// Every case here uses the real writer. Nothing is injected, so the fault is the
/// file handle and the recovery is the handle being released.
/// </summary>
public sealed class ClosedTabLockedFileTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("stride-closed-locked");
    private readonly string _path;

    public ClosedTabLockedFileTests()
    {
        _path = Path.Combine(_dir.FullName, "closed-tabs.json");
    }

    public void Dispose()
    {
        try { _dir.Delete(true); }
        catch { /* a handle a case left open must not fail the run */ }
    }

    /// <summary>The store the product builds, writing through the real atomic writer.</summary>
    private ClosedTabStore Store() => new(_path, ClosedTabStore.DefaultCapacity);

    /// <summary>Holds the file the way another program would: no sharing at all.</summary>
    private FileStream Hold() => new(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private static string[] Urls(ClosedTabStore store) => [.. store.Load().Select(r => r.Url)];

    /// <summary>What a fresh store reads from the file, which is what a restart sees.</summary>
    private string[] OnDisk() =>
        File.Exists(_path) ? [.. new ClosedTabStore(_path, 50).Load().Select(r => r.Url)] : [];

    [Fact]
    public void AFailedAppendUnderALockKeepsWhatIsAlreadyOnDisk()
    {
        var store = Store();
        Assert.True(store.Record("https://saved.example/", "Saved", false));
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));

        using (Hold())
        {
            // The write cannot land, so the answer is false.
            Assert.False(store.Record("https://pending.example/", "Pending", false));
            Assert.True(store.LastWriteFailed);

            // The closure on disk is still remembered even though the lock also
            // stops the store from reading the file back.
            Assert.Equal(
                new[] { "https://saved.example/", "https://pending.example/" },
                Urls(store));
        }

        // And after the lock goes, with no repair record in between. The file has
        // only the closure that reached it, because the other one never did.
        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/" },
            Urls(store));
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void TheReportedReproductionIsFixedForThePlainWriterToo()
    {
        // The reproduction that was reported drove the store with a plain
        // File.WriteAllText writer rather than the atomic one, so this walks the
        // same steps against that writer.
        var store = new ClosedTabStore(_path, 50, (p, c) => File.WriteAllText(p, c));

        Assert.True(store.Record("https://saved.example/", "Saved", false));

        using (Hold())
        {
            Assert.False(store.Record("https://pending.example/", "Pending", false));
            Assert.Equal(
                new[] { "https://saved.example/", "https://pending.example/" },
                Urls(store));
        }

        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/" },
            Urls(store));

        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://saved.example/", store.TakeNewest()!.Url);
        Assert.Empty(OnDisk());
    }

    [Fact]
    public void AFailedWriteIsNotAFailedRead()
    {
        // A refused write must not mark the history unreadable, or every later
        // write and delete is refused for the rest of the run.
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
        {
            store.Record("https://pending.example/", "Pending", false);
            store.Record("https://second.example/", "Second", false);
            Assert.False(store.Clear());

            Assert.False(store.LastReadFailed);
            Assert.True(store.LastWriteFailed);
        }

        Assert.False(store.LastReadFailed);
    }

    [Fact]
    public void APendingClosureIsPoppedAfterTheLockGoesWithoutWriting()
    {
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
            Assert.False(store.Record("https://pending.example/", "Pending", false));

        var before = File.GetLastWriteTimeUtc(_path);

        // The closure that never reached the file costs the file no write.
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_path));

        // The one that is on disk is taken by a real removal, which now lands.
        Assert.Equal("https://saved.example/", store.TakeNewest()!.Url);
        Assert.False(store.LastWriteFailed);
        Assert.Empty(OnDisk());
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ADurableClosureIsStillRefusedWhileTheLockIsHeld()
    {
        // The other half of the rule. While the file is held, a removal that is on
        // disk cannot land, so it is not reported.
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
        {
            store.Record("https://pending.example/", "Pending", false);
            Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);

            // The file cannot be read from here either, so the refusal is answered
            // from what this process already knew.
            Assert.Null(store.TakeNewest());
            Assert.Equal("https://saved.example/", Assert.Single(Urls(store)));
        }

        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void RepeatedFailedAppendsUnderOneLockAllSurvive()
    {
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
        {
            Assert.False(store.Record("https://one.example/", "One", false));
            Assert.False(store.Record("https://two.example/", "Two", false));
            Assert.False(store.Record("https://three.example/", "Three", false));

            Assert.Equal(
                new[] { "https://saved.example/", "https://one.example/", "https://two.example/", "https://three.example/" },
                Urls(store));
        }

        // The lock goes and the history drains in closure order, newest first.
        Assert.Equal("https://three.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://two.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://one.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://saved.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());
        Assert.Empty(OnDisk());
    }

    [Fact]
    public void AFailedClearUnderALockForgetsNeitherKind()
    {
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
        {
            Assert.False(store.Record("https://pending.example/", "Pending", false));
            Assert.False(store.Clear());

            Assert.Equal(
                new[] { "https://saved.example/", "https://pending.example/" },
                Urls(store));
        }

        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/" },
            Urls(store));
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
        Assert.Equal("https://saved.example/", store.TakeNewest()!.Url);
        Assert.Empty(OnDisk());
    }

    [Fact]
    public void AFailedClearUnderALockKeepsEveryPendingClosure()
    {
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
        {
            store.Record("https://one.example/", "One", false);
            store.Record("https://two.example/", "Two", false);

            Assert.False(store.Clear());
            Assert.False(store.Clear());

            Assert.Equal(
                new[] { "https://saved.example/", "https://one.example/", "https://two.example/" },
                Urls(store));
        }

        Assert.Equal(
            new[] { "https://saved.example/", "https://one.example/", "https://two.example/" },
            Urls(store));
    }

    [Fact]
    public void TheLockedWriteLeavesTheFileExactlyAsItWas()
    {
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);
        var bytes = File.ReadAllBytes(_path);

        using (Hold())
        {
            store.Record("https://pending.example/", "Pending", false);
            store.Clear();
            store.Clear();
        }

        // Nothing that failed reached the file, so a restart replays the same one
        // closure it would have replayed all along.
        Assert.Equal(bytes, File.ReadAllBytes(_path));
        Assert.Equal("https://saved.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void TheStoreWritesAgainOnItsOwnOnceTheLockIsGone()
    {
        // No repair record is needed. The first write after the lock goes lands, and
        // it carries the pending closures with it. It also takes the temporary file
        // the refused writes left behind with it.
        var store = Store();
        store.Record("https://saved.example/", "Saved", false);

        using (Hold())
            Assert.False(store.Record("https://pending.example/", "Pending", false));

        Assert.True(store.Record("https://later.example/", "Later", false));

        Assert.False(store.LastWriteFailed);
        Assert.False(store.LastReadFailed);
        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal(
            new[] { "https://saved.example/", "https://pending.example/", "https://later.example/" },
            OnDisk());
    }

    [Fact]
    public void AFileThatWasNeverReadIsStillRefusedWhileItIsHeld()
    {
        // The other side of keeping the known cache: there is nothing known here, so
        // nothing durable may be claimed, and the refusal stays while the file
        // cannot be read.
        File.WriteAllText(_path, "[]");
        var store = Store();

        using (Hold())
        {
            Assert.Empty(store.Load());
            Assert.True(store.LastReadFailed);

            Assert.False(store.Record("https://pending.example/", "Pending", false));

            // The one closure that was made is still reopenable, and nothing was
            // invented for the half that could not be read.
            Assert.Equal("https://pending.example/", Assert.Single(Urls(store)));
        }

        // The hold went away. The read that failed here was a real failed read, so
        // the refusal stays until something can read the file. That refusal costs
        // the pending closure nothing: it is still there and still costs the file no
        // write.
        Assert.True(store.LastReadFailed);
        Assert.Equal("https://pending.example/", store.TakeNewest()!.Url);
        Assert.Null(store.TakeNewest());

        // A store that starts after the hold reads the file and writes normally.
        var recovered = Store();
        Assert.False(recovered.LastReadFailed);
        Assert.True(recovered.Record("https://after.example/", "After", false));
        Assert.Equal("https://after.example/", Assert.Single(OnDisk()));
    }

    [Fact]
    public void ACorruptFileHeldOpenIsStillNotOverwritten()
    {
        const string original = "{ this is not json at all";
        File.WriteAllText(_path, original);
        var store = Store();

        using (Hold())
        {
            Assert.Empty(store.Load());
            Assert.True(store.LastReadFailed);

            Assert.False(store.Record("https://pending.example/", "Pending", false));
            Assert.Equal("https://pending.example/", Assert.Single(Urls(store)));
        }

        // The bytes are the only surviving record of whatever was in that file, so
        // nothing this run knew about them may replace them.
        Assert.Equal(original, File.ReadAllText(_path));
        Assert.True(store.LastReadFailed);
        Assert.False(store.Record("https://another.example/", "Another", false));
        Assert.False(store.Clear());
        Assert.False(store.ClearAndDelete());
        Assert.Equal(original, File.ReadAllText(_path));

        // Both closures this run made are still reopenable, and the durable half is
        // still empty because the file has never been readable.
        Assert.Equal(
            new[] { "https://pending.example/", "https://another.example/" },
            Urls(store));
    }
}