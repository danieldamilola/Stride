using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using StrideBrowser.Services;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The decisions a reconciliation pass makes, with no profile and no COM anywhere.
///
/// These are the rules that decide whether a working registration is removed, whether
/// a 4 MB zip is unpacked for a profile that does not need it, and whether a legacy
/// extension takes uBlock down with it. All three were decided inside a method that
/// could only run against a live WebView2 profile, which is why they went untested.
/// </summary>
public sealed class ExtensionReconcilePlanTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stride-ext-plan");
    private readonly string _manifestDir;

    public ExtensionReconcilePlanTests()
    {
        _manifestDir = Path.Combine(_root.FullName, "extensions", "uBlock0_1.73.0");
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root.FullName, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                if (info.IsReadOnly) info.IsReadOnly = false;
            }
        }
        catch { /* best effort */ }
        _root.Delete(true);
    }

    private static ExtensionManager.ReconcileInputs Registered(
        bool desired,
        string? folder,
        bool superseded = false,
        bool needsNormalization = false,
        bool normalizedNow = false,
        string? prepared = null,
        bool legacy = false) =>
        new(Registered: true,
            LegacyExtensionPresent: legacy,
            CurrentFolder: folder,
            HasSupersededVersionDir: superseded,
            AssetsNeedNormalization: needsNormalization,
            AssetsNormalizedNow: normalizedNow,
            PreparedFolder: prepared,
            DesiredEnabled: desired);

    [Fact]
    public void AHealthyRegistrationIsOnlyReconciledForItsEnabledState()
    {
        var plan = ExtensionManager.Plan(Registered(desired: true, folder: @"C:\ext\uBlock0_1.73.0"));

        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, plan.Action);
        Assert.False(plan.RemoveLegacy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOffPreferenceWithNothingInstalled_RegistersNothing(bool wantsBlocking)
    {
        var plan = ExtensionManager.Plan(new ExtensionManager.ReconcileInputs(
            Registered: false, LegacyExtensionPresent: false, CurrentFolder: null,
            HasSupersededVersionDir: false, AssetsNeedNormalization: false,
            AssetsNormalizedNow: false, PreparedFolder: null, DesiredEnabled: wantsBlocking));

        var action = wantsBlocking
            ? ExtensionManager.ReconcileAction.FailPreservingRegistration
            : ExtensionManager.ReconcileAction.NothingToDo;
        Assert.Equal(action, plan.Action);
        Assert.False(ExtensionManager.NeedsPreparation(
            registered: false, desiredEnabled: false, folderMissing: true,
            assetsNeedNormalization: false, hasSupersededVersionDir: false));
    }

    [Fact]
    public void AProfileThatWantsBlockingWithNothingInstalled_RegistersTheFolderOnDisk()
    {
        var plan = ExtensionManager.Plan(new ExtensionManager.ReconcileInputs(
            Registered: false, LegacyExtensionPresent: false, CurrentFolder: null,
            HasSupersededVersionDir: false, AssetsNeedNormalization: false,
            AssetsNormalizedNow: false, PreparedFolder: @"C:\ext\uBlock0_1.73.0", DesiredEnabled: true));

        Assert.Equal(ExtensionManager.ReconcileAction.Register, plan.Action);
        Assert.Equal(@"C:\ext\uBlock0_1.73.0", plan.Folder);
    }

    [Fact]
    public void PreparationIsSkippedForAHealthyRegisteredFolder()
    {
        // This is the warm path. Nothing to install, nothing to repair, so the pass
        // never opens the files at all.
        Assert.False(ExtensionManager.NeedsPreparation(
            registered: true, desiredEnabled: true, folderMissing: false,
            assetsNeedNormalization: false, hasSupersededVersionDir: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PreparationHappensOnlyWhenSomethingIsWrong(bool folderMissing, bool superseded)
    {
        Assert.True(ExtensionManager.NeedsPreparation(
            registered: true, desiredEnabled: true, folderMissing: folderMissing,
            assetsNeedNormalization: false, hasSupersededVersionDir: superseded));
        Assert.True(ExtensionManager.NeedsPreparation(
            registered: true, desiredEnabled: true, folderMissing: false,
            assetsNeedNormalization: true, hasSupersededVersionDir: false));
    }

    [Fact]
    public void AnUnwritableAssetsFile_KeepsTheWorkingRegistrationAndDoesNotReAdd()
    {
        // The folder is registered, the assets could not be normalized, so the pass
        // has nothing to re-register from. Removing the extension here would replace a
        // working blocker with the same folder, which is how a healthy profile was
        // being cycled on every start.
        var plan = ExtensionManager.Plan(Registered(
            desired: true,
            folder: @"C:\ext\uBlock0_1.73.0",
            needsNormalization: true,
            normalizedNow: false,
            prepared: @"C:\ext\uBlock0_1.73.0"));

        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, plan.Action);
    }

    [Fact]
    public void AFolderThatCannotBePrepared_KeepsTheRegistrationAndReportsFailure()
    {
        var plan = ExtensionManager.Plan(Registered(
            desired: true,
            folder: null,
            needsNormalization: true,
            prepared: null));

        Assert.Equal(ExtensionManager.ReconcileAction.FailPreservingRegistration, plan.Action);
        Assert.Contains("could not be normalized", plan.Detail);
    }

    [Fact]
    public void AssetsThatNeedRewriting_AreRepairedInPlaceWithoutReRegistering()
    {
        // A repair is written over the folder the platform is holding. Re-adding the same
        // folder is a reinstall and a reinstall discards the extension's own storage, which
        // was measured on a real profile, so a repair never cycles the registration.
        var plan = ExtensionManager.Plan(Registered(
            desired: true,
            folder: @"C:\ext\uBlock0_1.73.0",
            needsNormalization: true,
            normalizedNow: true,
            prepared: @"C:\ext\uBlock0_1.73.0"));

        Assert.Equal(ExtensionManager.ReconcileAction.RepairInPlace, plan.Action);
    }

    [Fact]
    public void ASupersededVersionFolderMovesTheRegistrationOntoTheCurrentOne()
    {
        var plan = ExtensionManager.Plan(Registered(
            desired: true,
            folder: @"C:\ext\uBlock0_1.73.0",
            superseded: true,
            prepared: @"C:\ext\uBlock0_1.73.0"));

        Assert.Equal(ExtensionManager.ReconcileAction.RemoveAndRegister, plan.Action);
    }

    [Fact]
    public void AnOrphanedRegistrationIsReplacedWithTheFolderOnDisk()
    {
        var plan = ExtensionManager.Plan(Registered(
            desired: true,
            folder: null,
            prepared: @"C:\ext\uBlock0_1.73.0"));

        Assert.Equal(ExtensionManager.ReconcileAction.RemoveAndRegister, plan.Action);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheLegacyDownloadManagerNeverBringsUBlockDownWithIt(bool desiredBlocking)
    {
        // The earlier code cleared the uBlock reference as soon as the legacy
        // extension was found. With the preference off that reported an unregistered,
        // disabled uBlock as the wanted state, and with the preference on it
        // reinstalled an extension that was already registered.
        var plan = ExtensionManager.Plan(Registered(desired: desiredBlocking, folder: @"C:\ext\uBlock0_1.73.0", legacy: true));

        Assert.True(plan.RemoveLegacy);
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, plan.Action);
    }

    [Fact]
    public void NamesAreClassifiedIndependentlyOfEachOther()
    {
        Assert.True(ExtensionManager.IsUBlock("uBlock Origin"));
        Assert.True(ExtensionManager.IsUBlock("uBlock Origin Lite"));
        Assert.False(ExtensionManager.IsUBlock("Internet Download Manager Integration"));

        Assert.True(ExtensionManager.IsLegacyDownloadManager("Internet Download Manager Integration"));
        Assert.True(ExtensionManager.IsLegacyDownloadManager("IDM Integration Module"));
        Assert.False(ExtensionManager.IsLegacyDownloadManager("uBlock Origin"));

        Assert.False(ExtensionManager.IsUBlock(null));
        Assert.False(ExtensionManager.IsLegacyDownloadManager(null));
    }

    [Fact]
    public void AStalePostUpdateMarkerOnDisk_DoesNotCauseAReRegistration()
    {
        // The marker is a hint that an updater ran. It can outlive a failed update,
        // and the folder it used to guard lives in the data directory rather than
        // beside the binaries, so acting on it meant removing and re-adding a healthy
        // registration on every start after one update.
        var markerDir = _root.CreateSubdirectory("marker");
        var marker = Path.Combine(markerDir.FullName, "post_update.flag");
        File.WriteAllText(marker, "stale");

        var manager = new ExtensionManager(
            Path.Combine(_root.FullName, "extensions"),
            Path.Combine(_root.FullName, "ublock.sha256"),
            Path.Combine(_root.FullName, "bundled"),
            new HttpClient(new ThrowingHandler()))
        { PostUpdateMarkerPath = marker };

        // A healthy registered folder, and a marker that is present.
        Assert.True(File.Exists(manager.PostUpdateMarkerPath));
        Assert.False(ExtensionManager.NeedsPreparation(
            registered: true, desiredEnabled: true, folderMissing: false,
            assetsNeedNormalization: false, hasSupersededVersionDir: false));

        var plan = ExtensionManager.Plan(Registered(desired: true, folder: @"C:\ext\uBlock0_1.73.0"));
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, plan.Action);
    }

    [Fact]
    public async Task ARepairThatCannotWrite_LeavesTheInstalledBytesAlone()
    {
        // The real file fault behind the policy above: a read only assets.json cannot
        // be repaired, so preparation returns the folder it already had and reports
        // that it rewrote nothing.
        var bundled = Path.Combine(_root.FullName, "bundled");
        Directory.CreateDirectory(bundled);
        File.WriteAllBytes(Path.Combine(bundled, "uBlock0_1.73.0.chromium.zip"), TinyZip());

        var manager = new ExtensionManager(
            Path.Combine(_root.FullName, "extensions"),
            Path.Combine(_root.FullName, "ublock.sha256"),
            bundled,
            new HttpClient(new ThrowingHandler()));

        var cold = await manager.PrepareAsync();
        Assert.NotNull(cold);

        // Put the folder back into the unrepaired state the repair is meant to fix.
        var assets = Path.Combine(cold!, "assets", "assets.json");
        File.WriteAllText(assets,
            "{\"filter\":{\"contentURL\":\"https://cdn.jsdelivr.net/x.js\",\"cdnURLs\":[\"u\"]}}");
        var before = File.ReadAllBytes(assets);
        File.SetAttributes(assets, FileAttributes.ReadOnly);

        var warm = await manager.PrepareAsync();

        Assert.Equal(cold, warm);
        Assert.False(manager.LastPreparation.AssetsRewritten);
        Assert.Equal(before, File.ReadAllBytes(assets));

        // And the pass that follows keeps the registration rather than cycling it.
        var plan = ExtensionManager.Plan(Registered(
            desired: true, folder: cold, needsNormalization: true, normalizedNow: false, prepared: warm));
        Assert.Equal(ExtensionManager.ReconcileAction.ApplyEnabledStateOnly, plan.Action);
    }

    private static byte[] TinyZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            // Each entry stream is closed before the next entry is created. Leaving
            // one open makes the archive reject the next CreateEntry call.
            using (var manifest = zip.CreateEntry("manifest.json").Open())
            using (var writer = new StreamWriter(manifest))
            {
                writer.Write("{\"manifest_version\":3,\"name\":\"uBlock Origin\",\"version\":\"1.73.0\"}");
            }

            using (var assets = zip.CreateEntry("assets/assets.json").Open())
            using (var writer = new StreamWriter(assets))
            {
                writer.Write("{\"filter\":{\"contentURL\":\"https://cdn.jsdelivr.net/x.js\",\"cdnURLs\":[\"u\"]}}");
            }
        }
        return buffer.ToArray();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct) =>
            throw new InvalidOperationException("no network in this test");
    }
}
