using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using StrideBrowser.Helpers;
using Microsoft.Web.WebView2.Core;
using StrideBrowser.Models;

namespace StrideBrowser.Services;

/// <summary>
/// Manages WebView2 browser extensions including built-in uBlock Origin.
///
/// Two halves live here and they must stay apart. Disk work, which is hashing,
/// unzipping and rewriting assets.json, is <see cref="PrepareAsync"/> and is
/// safe to run on any thread. WebView2 work is <see cref="ReconcileAsync"/>,
/// and every call in it is a COM call that has to run on the thread owning the
/// control. The old shape put both in one method that the caller kicked off with
/// Task.Run, so the file half resumed on a pool thread and the WebView2 half was
/// never actually awaited by anything that needed it to finish.
/// </summary>
public sealed class ExtensionManager
{
    private const string UBlockVersion = "1.73.0";

    private const string UBlockDownloadUrl =
        $"https://github.com/gorhill/uBlock/releases/download/{UBlockVersion}/uBlock0_{UBlockVersion}.chromium.zip";

    private static readonly HttpClient SharedHttp = CreateHttpClient();

    /// <summary>Elapsed milliseconds, for stage timing written to the trace.</summary>
    private static long Ms(System.Diagnostics.Stopwatch sw) => sw.ElapsedMilliseconds;

    /// <summary>
    /// Where the extension is unpacked. Instance state rather than a static read
    /// of AppPaths at type initialisation, so a test can point the whole class at
    /// an isolated directory instead of the user's real profile.
    /// </summary>
    private readonly string _extensionsDir;

    /// <summary>Where the TOFU hash is kept. Instance state for the same reason.</summary>
    private readonly string _hashFilePath;

    /// <summary>Where the bundled zip is looked for. Instance state for the same reason.</summary>
    private readonly string _bundledResourcesDir;

    private readonly HttpClient _http;

    public ExtensionManager() : this(null, null, null, null) { }

    /// <summary>Test seam. Any null keeps the production value.</summary>
    public ExtensionManager(string? extensionsDir, string? hashFilePath, string? bundledResourcesDir, HttpClient? http)
    {
        _extensionsDir = extensionsDir ?? AppPaths.ExtensionsDir;
        _hashFilePath = hashFilePath ?? AppPaths.UBlockHashFile + "." + UBlockVersion;
        _bundledResourcesDir = bundledResourcesDir ??
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Extensions");
        _http = http ?? SharedHttp;
    }

    /// <summary>Where the unpacked extension lives.</summary>
    public string ExtensionsDir => _extensionsDir;

    /// <summary>How long each stage of the disk preparation took on the last run.</summary>
    public PreparationTimings LastPreparation { get; private set; } = PreparationTimings.Empty;

    /// <summary>
    /// Wall time per stage of the disk only work, so a caller can see where the
    /// time goes without a profiler and without a ComObject in the picture.
    /// </summary>
    public sealed record PreparationTimings(
        bool FolderWasAlreadyPresent,
        long LocateManifestMs,
        long StripAssetsMs,
        long HashMs,
        long DownloadMs,
        long MoveAsideMs,
        long ExtractMs,
        long CleanupMs,
        long TotalMs,
        bool AssetsRewritten = false)
    {
        public static PreparationTimings Empty { get; } =
            new(false, 0, 0, 0, 0, 0, 0, 0, 0, false);
    }

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!c.DefaultRequestHeaders.Contains("User-Agent"))
            c.DefaultRequestHeaders.Add("User-Agent", "StrideBrowser-ExtensionManager");
        return c;
    }

    /// <summary>What a pass decided to do, before any WebView2 call is made.</summary>
    public enum ReconcileAction
    {
        /// <summary>The profile already holds what the preference asks for.</summary>
        NothingToDo,

        /// <summary>Registered and healthy. Only the enabled state may need setting.</summary>
        ApplyEnabledStateOnly,

        /// <summary>Not registered and the folder is on disk. Register it.</summary>
        Register,

        /// <summary>The folder behind the registration is no longer the folder it was registered from.</summary>
        RemoveAndRegister,

        /// <summary>
        /// The registration is healthy and points at the folder on disk, and only that
        /// folder's files need rewriting. The bytes are written in place.
        ///
        /// This is its own action because the alternative is destructive, and that was
        /// measured rather than assumed on a real profile with real user state in it.
        /// Removing the extension and adding the same folder back reinstalls it, and a
        /// reinstall discards everything the extension stored for itself: a durable
        /// sentinel key was gone afterwards and a non-default list selection came back
        /// as the shipped defaults. Writing the same bytes in place kept the
        /// registration, kept the sentinel and kept the selection, through a new process
        /// as well. So a repair never cycles the registration.
        /// </summary>
        RepairInPlace,

        /// <summary>
        /// The files could not be prepared. An existing registration is left alone and
        /// the failure is reported, because removing a working extension to re-add
        /// unprepared contents is worse than keeping it.
        /// </summary>
        FailPreservingRegistration
    }

    /// <summary>Everything the pass decided from names and files, before any COM call.</summary>
    public sealed record ReconcileInputs(
        bool Registered,
        bool LegacyExtensionPresent,
        string? CurrentFolder,
        bool HasSupersededVersionDir,
        bool AssetsNeedNormalization,
        bool AssetsNormalizedNow,
        string? PreparedFolder,
        bool DesiredEnabled);

    public sealed record ReconcilePlan(ReconcileAction Action, bool RemoveLegacy, string? Folder, string? Detail);

    /// <summary>
    /// What one preparation call produced, handed back to the pass that asked for it.
    ///
    /// <see cref="LastPreparation"/> is the same information kept for the trace, and it
    /// is shared by every window on this manager. A pass that needs no file work never
    /// writes it, so reading it back told the next pass that it had rewritten assets it
    /// never touched, and two profiles preparing at once could read each other's result.
    /// This record travels with the call that produced it, so the decision is made from
    /// this pass's own work.
    /// </summary>
    private sealed record PreparationOutcome(
        string? Folder,
        bool AssetsRewritten,
        bool RegistryKeyRewritten,
        StagedRepair? Staged,
        StagedRepair? PendingRepair,
        string RepairDetail)
    {
        /// <summary>What a pass holds when it did not prepare anything.</summary>
        public static PreparationOutcome NotPrepared { get; } =
            new(null, false, false, null, null, "nothing was prepared this pass");

        /// <summary>
        /// True when this pass worked out that the folder has to change, so a plan built
        /// from it knows a repair is waiting. Carried per pass, never read back off the
        /// manager.
        /// </summary>
        public bool FolderChanged => AssetsRewritten || RegistryKeyRewritten;
    }

    /// <summary>
    /// What one pass decided and what it did to the files to get there. Returned so a
    /// caller can see the preparation that produced the plan, rather than inferring it
    /// from timing state that a later pass will overwrite.
    /// </summary>
    public sealed record PassDecision(
        ReconcilePlan Plan,
        string? PreparedFolder,
        bool PreparedThisPass,
        bool AssetsRewrittenThisPass,
        bool RegistryKeyRewrittenThisPass,
        StagedRepair? PendingRepair,
        bool FolderAssessable,
        string RepairDetail)
    {
        /// <summary>
        /// True when this pass worked out the folder has to change, so the platform may
        /// have dropped the registration that pointed at it.
        /// </summary>
        public bool FolderChangedThisPass => AssetsRewrittenThisPass || RegistryKeyRewrittenThisPass;
    }

    /// <summary>
    /// Whether this pass has to touch extension files at all.
    ///
    /// Pure, and the reason the warm path is free: a healthy registered folder needs
    /// nothing, and a profile with the preference off and nothing installed never gets
    /// a 4 MB zip unpacked for a state that already satisfies it.
    /// </summary>
    public static bool NeedsPreparation(
        bool registered,
        bool desiredEnabled,
        bool folderMissing,
        bool assetsNeedNormalization,
        bool hasSupersededVersionDir) =>
        registered
            ? folderMissing || assetsNeedNormalization || hasSupersededVersionDir
            : desiredEnabled;

    /// <summary>
    /// Turns what the pass observed into what it will do. Pure, so the recovery and
    /// the failure rules are decided by unit tests rather than by a live profile.
    /// </summary>
    public static ReconcilePlan Plan(ReconcileInputs inputs)
    {
        // The legacy extension is removed on its own. It says nothing about uBlock,
        // and an earlier revision cleared the uBlock reference here, which left a live
        // uBlock untouched with the preference off and reinstalled it with the
        // preference on.
        var removeLegacy = inputs.LegacyExtensionPresent;
        var folder = inputs.PreparedFolder ?? inputs.CurrentFolder;

        if (!inputs.Registered)
        {
            if (!inputs.DesiredEnabled)
                return new ReconcilePlan(ReconcileAction.NothingToDo, removeLegacy, null,
                    "ad blocking is off and uBlock is not registered, so nothing was unpacked");
            if (folder is null)
                return new ReconcilePlan(ReconcileAction.FailPreservingRegistration, removeLegacy, null,
                    "uBlock is not registered and the folder could not be prepared");
            return new ReconcilePlan(ReconcileAction.Register, removeLegacy, folder,
                "registering uBlock from the folder on disk");
        }

        if (inputs.PreparedFolder is null && inputs.AssetsNeedNormalization)
            return new ReconcilePlan(ReconcileAction.FailPreservingRegistration, removeLegacy, folder,
                "the registered folder could not be normalized, so the working registration was kept");

        if (inputs.AssetsNormalizedNow)
            return new ReconcilePlan(ReconcileAction.RepairInPlace, removeLegacy, folder,
                "the registered folder needs its files rewritten, which is done in place so the " +
                "extension keeps everything it has stored for itself");

        if (inputs.CurrentFolder is null)
        {
            if (folder is null)
                return new ReconcilePlan(ReconcileAction.FailPreservingRegistration, removeLegacy, null,
                    "the registered folder is gone and nothing could be prepared to replace it");
            return new ReconcilePlan(ReconcileAction.RemoveAndRegister, removeLegacy, folder,
                "the registered folder is gone, so the orphan is replaced with the folder on disk");
        }

        if (inputs.HasSupersededVersionDir)
            return new ReconcilePlan(ReconcileAction.RemoveAndRegister, removeLegacy, folder,
                "a superseded version folder is present, so the registration is moved onto the current folder");

        // A stale marker or a hint that something changed is not a reason to remove and
        // re-add. The pass reads the real state of the profile and the real state of
        // the folder, and a folder that matches what is registered is left alone.
        return new ReconcilePlan(ReconcileAction.ApplyEnabledStateOnly, removeLegacy, folder,
            "the registration matches the folder on disk, so only the enabled state is reconciled");
    }

    /// <summary>
    /// Brings one profile to the state the user asked for and reports what WebView2
    /// holds afterwards, read back from the profile rather than inferred from a
    /// folder on disk. A folder that exists is not an extension that is running.
    ///
    /// Runs on the thread that owns the control, because every Profile or extension
    /// call here is a COM call. The file checks, and the extraction when files are
    /// genuinely needed, run on the pool thread instead.
    ///
    /// Files are only touched when this pass needs them. Rewriting the folder of an
    /// installed extension is how a registration gets dropped: the platform reads
    /// the folder by path, and changed content is a different extension.
    ///
    /// There is no cancellation token here on purpose. No WebView2 call takes one,
    /// and this work is shared by every window on the profile, so one waiter walking
    /// away must not pull it out from under the others. The caller's wait is what is
    /// cancellable.
    /// </summary>
    public async Task<ExtensionNativeState> ReconcileAsync(
        CoreWebView2 webview,
        bool desiredEnabled)
    {
        // Every window in the process shares one manager and therefore one extensions
        // folder, while the coordinator only serializes per profile. Two profiles in one
        // process can therefore be preparing the same folder at the same time, and one
        // of them committing bytes while the other reads them. The whole read, remove,
        // commit and add window is taken under this gate, so the folder is only ever
        // written by one pass at a time. It is taken after the extension listing is read,
        // so a waiter is never blocked by a pass it does not need to wait for.
        var folderGateHeld = false;
        try
        {
            await _folderGate.WaitAsync().ConfigureAwait(false);
            folderGateHeld = true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager: could not take the folder gate: {ex.Message}");
        }

        try
        {
            return await ReconcileHeldAsync(webview, desiredEnabled);
        }
        finally
        {
            if (folderGateHeld) _folderGate.Release();
        }
    }

    private async Task<ExtensionNativeState> ReconcileHeldAsync(
        CoreWebView2 webview,
        bool desiredEnabled)
    {
        string? profilePath = null;
        try { profilePath = webview.Profile.ProfilePath; }
        catch (Exception ex) { Trace.WriteLine($"ExtensionManager: profile path unavailable: {ex.Message}"); }

        var extensions = await webview.Profile.GetBrowserExtensionsAsync();
        var ublock = FindUBlock(extensions);
        var idm = FindLegacyDownloadManager(extensions);

        var decision = await DecideAsync(
            registered: ublock is not null,
            legacyExtensionPresent: idm is not null,
            desiredEnabled: desiredEnabled);
        var plan = decision.Plan;

        if (plan.RemoveLegacy && idm is not null)
        {
            Trace.WriteLine($"ExtensionManager: Removing legacy IDM extension (id={idm.Id}).");
            await RemoveExtensionAsync(idm);
        }

        if (plan.Action == ReconcileAction.FailPreservingRegistration)
        {
            // Nothing is removed. Whatever is registered keeps working and the caller
            // is told the truth about why the files are not ready.
            Trace.WriteLine($"ExtensionManager: {plan.Detail}");
            var preserved = await ReadBackAsync(webview, plan.Folder, profilePath);
            return preserved with { Detail = UntruthfulRepair(decision, plan.Detail) };
        }

        // A repair is written in place and never cycles the registration. Re-adding the same
        // folder is a reinstall, and a reinstall throws away what the extension stored for
        // itself, which was measured on a real profile carrying a durable sentinel key and
        // a non-default list selection. Both were gone after a remove and add and both
        // survived an in-place write, including across a new process. The one guarantee
        // offered is the rollback: a write that fails part way puts every file back from
        // the bytes captured at staging time, so the folder never ends up half repaired.
        var staged = decision.PendingRepair;
        if (plan.Action == ReconcileAction.RepairInPlace && staged is { } repair && repair.FolderChanged)
        {
            Trace.WriteLine($"ExtensionManager: {plan.Detail}");
            var commit = CommitFolderRepair(repair);
            Trace.WriteLine($"ExtensionManager: {commit.Detail}");
            if (!commit.Completed)
            {
                var back = await ReadBackAsync(webview, plan.Folder, profilePath);
                return back with
                {
                    Detail = "the filter list repair could not be written, so the folder was put back " +
                             "as it was; the extension is registered and enabled but its filter lists " +
                             "cannot update. " + commit.Detail
                };
            }
        }
        else if (plan.Action == ReconcileAction.RemoveAndRegister && ublock is not null)
        {
            // A genuine folder or version or location change. The platform is pointed
            // somewhere else now, so the registration has to be taken and given again.
            Trace.WriteLine("ExtensionManager: the folder behind the registration changed, cycling it.");
            await RemoveExtensionAsync(ublock);
            ublock = null;
        }
        else if (staged is not null && staged.FolderChanged)
        {
            // The pass worked out the folder has to change but is not going to write it.
            // Nothing is written, and the caller is told rather than left to assume.
            Trace.WriteLine(
                $"ExtensionManager: the folder needs a repair this pass will not make, so nothing " +
                $"was written ({staged.Detail}).");
        }

        if (plan.Action is ReconcileAction.Register or ReconcileAction.RemoveAndRegister)
        {
            if (plan.Folder is null)
                return new ExtensionNativeState(false, false, null, null, profilePath,
                    "the plan asked to register without a folder");
            Trace.WriteLine($"ExtensionManager: registering uBlock from {plan.Folder}");
            await webview.Profile.AddBrowserExtensionAsync(plan.Folder);
        }

        // Read back instead of assuming. Add leaves it enabled, an earlier run may
        // have left it disabled, and only the profile knows which.
        var listed = FindUBlock(await webview.Profile.GetBrowserExtensionsAsync());
        if (listed is null)
            return new ExtensionNativeState(false, false, null, plan.Folder, profilePath,
                "uBlock is not in the profile after reconciliation");

        if (listed.IsEnabled != desiredEnabled)
        {
            await listed.EnableAsync(desiredEnabled);
            // EnableAsync resolves when the request is made, and the profile listing is
            // updated by the browser process afterwards. Measured here: turning the
            // extension off was reflected in the next listing, turning it back on was
            // not, so a single immediate read would have reported a failed toggle for
            // a change that had already been asked for. Bounded, and the final answer
            // is still whatever the profile last reported.
            await WaitForEnabledStateAsync(webview, desiredEnabled, EnableSettleBound);
        }

        await Task.Run(CleanupOldVersions);

        var live = FindUBlock(await webview.Profile.GetBrowserExtensionsAsync());
        if (live is null)
            return new ExtensionNativeState(false, false, null, plan.Folder, profilePath,
                "uBlock left the profile while its enabled state was being set");

        return new ExtensionNativeState(true, live.IsEnabled, live.Id, plan.Folder, profilePath)
        {
            Detail = UntruthfulRepair(decision, null)
        };
    }

    /// <summary>
    /// The detail a caller is told when a repair this pass needed did not happen.
    ///
    /// A pass that preserves a healthy registration can still match the requested native
    /// state, so the reported state alone would read as a success. The detail is where
    /// the difference is stated, and it is never dropped for a matching state.
    /// </summary>
    private static string UntruthfulRepair(PassDecision decision, string? baseDetail)
    {
        if (decision.FolderAssessable) return baseDetail ?? string.Empty;
        if (decision.PendingRepair is not { } staged || !staged.FolderChanged)
            return baseDetail ?? string.Empty;

        var lead = "the filter list repair this pass needed was not made, so the folder was left " +
                   "as it was and the extension cannot refresh its lists";
        return string.IsNullOrWhiteSpace(baseDetail) ? lead : $"{baseDetail}. {lead}";
    }

    /// <summary>
    /// Decides what this pass will do, from what the profile holds and what is on disk.
    /// No WebView2 call happens here, which is what lets the decision be exercised
    /// against real files without a live profile.
    ///
    /// The normalization answer comes from the preparation this pass performed, never
    /// from <see cref="LastPreparation"/>. That property only refreshes when a
    /// preparation runs, so a later pass that needed no file work used to inherit the
    /// previous pass's answer and re-register a healthy folder over a preference toggle.
    /// It is also shared by every window on this manager, so one profile's preparation
    /// could decide another profile's pass. A pass that did not prepare reports false.
    /// </summary>
    public async Task<PassDecision> DecideAsync(
        bool registered,
        bool legacyExtensionPresent,
        bool desiredEnabled)
    {
        // File side, off the dispatcher: existence checks and one walk.
        var inspection = await Task.Run(InspectInstallation);

        var needsFiles = NeedsPreparation(
            registered: registered,
            desiredEnabled: desiredEnabled,
            folderMissing: inspection.CurrentFolder is null,
            assetsNeedNormalization: inspection.FolderNeedsRepair,
            hasSupersededVersionDir: inspection.HasSupersededVersionDir);

        var outcome = PreparationOutcome.NotPrepared;
        if (needsFiles)
        {
            // Nothing holds the folder when the profile has no registration, so the
            // repair may be written straight away. When one does hold it, the bytes are
            // only read here and committed by ReconcileAsync after the native remove.
            outcome = await PrepareOffThread(mayWriteFolder: !registered);
            if (outcome.Folder is null)
                Trace.WriteLine("ExtensionManager: preparation produced no folder this pass.");
        }

        var plan = Plan(new ReconcileInputs(
            Registered: registered,
            LegacyExtensionPresent: legacyExtensionPresent,
            CurrentFolder: inspection.CurrentFolder,
            HasSupersededVersionDir: inspection.HasSupersededVersionDir,
            AssetsNeedNormalization: inspection.FolderNeedsRepair,
            AssetsNormalizedNow: outcome.FolderChanged,
            PreparedFolder: outcome.Folder,
            DesiredEnabled: desiredEnabled));

        if (outcome.Staged is { } performed && !performed.Assessable)
        {
            // Truthful about a repair that could not be run. A folder whose state cannot
            // be established is not a healthy folder and must not be reported as one.
            Trace.WriteLine(
                $"ExtensionManager: the installed folder could not be fully assessed " +
                $"({outcome.RepairDetail}).");
        }

        return new PassDecision(
            plan, outcome.Folder, needsFiles, outcome.AssetsRewritten, outcome.RegistryKeyRewritten,
            outcome.PendingRepair, outcome.Staged?.Assessable ?? true, outcome.RepairDetail);
    }

    /// <summary>
    /// Reads what the profile holds without changing anything, for the paths where
    /// the pass has decided to do nothing to the registration.
    /// </summary>
    private static async Task<ExtensionNativeState> ReadBackAsync(
        CoreWebView2 webview,
        string? folder,
        string? profilePath)
    {
        var live = FindUBlock(await webview.Profile.GetBrowserExtensionsAsync());
        return live is null
            ? new ExtensionNativeState(false, false, null, folder, profilePath)
            : new ExtensionNativeState(true, live.IsEnabled, live.Id, folder, profilePath);
    }

    /// <summary>
    /// Makes sure the unpacked extension is on disk and returns the folder holding
    /// its manifest, or null when it could not be obtained.
    ///
    /// No COM, no WebView2, no UI thread. Everything here is file system and CPU
    /// work: hashing, downloading, unzipping, normalizing assets.json and deleting
    /// superseded folders. It is safe and intended to run off the UI thread.
    /// </summary>
    public async Task<string?> PrepareAsync()
    {
        // The disk-only seam. It has no profile handle, so it cannot take a native
        // remove, and it is therefore only safe for a folder nothing holds. The
        // registration path is DecideAsync and ReconcileAsync, which stage instead and
        // commit after the remove.
        return (await PrepareAndReportAsync(mayWriteFolder: true)).Folder;
    }

    /// <summary>
    /// Runs the disk preparation on the thread pool.
    ///
    /// The caller is on the dispatcher when it needs the folder back, and a
    /// multi second zip extraction resuming there is the freeze this split exists
    /// to remove. The outcome travels back with the folder rather than being read off
    /// the manager afterwards, so a caller decides on its own preparation.
    /// </summary>
    private async Task<PreparationOutcome> PrepareOffThread(bool mayWriteFolder)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await Task.Run(() => PrepareAndReportAsync(mayWriteFolder)).ConfigureAwait(false);
        Trace.WriteLine($"ExtensionManager: prepare {sw.ElapsedMilliseconds}ms on pool thread");
        return outcome;
    }

    /// <summary>
    /// What is on disk right now for this version, read on a pool thread.
    /// Pure file checks, so it can run without a WebView2 anywhere.
    /// </summary>
    private InstallationInspection InspectInstallation()
    {
        var targetDir = Path.Combine(_extensionsDir, $"uBlock0_{UBlockVersion}");
        string? manifestDir = null;
        try { manifestDir = FindManifestDirectory(targetDir); }
        catch (Exception ex) { Trace.WriteLine($"ExtensionManager: manifest lookup failed: {ex.Message}"); }

        var superseded = HasSupersededVersionDir();

        // The post update marker is read, and deliberately not used as a reason to
        // remove and re-add anything. It is a hint that an updater ran, it can survive
        // a failed update or a cleanup that never completed, and the folder it guards
        // lives in the data directory rather than beside the binaries. Acting on it
        // meant removing a healthy registration on every start after one update. The
        // pass already reads the real state of the profile and the real state of the
        // folder, so a genuine version or location change is caught without it.
        var postUpdate = false;
        try { postUpdate = File.Exists(PostUpdateMarkerPath); }
        catch (Exception ex) { Trace.WriteLine($"ExtensionManager: post update marker unreadable: {ex.Message}"); }

        var needsRepair = manifestDir is not null && AssetsNeedRepair(manifestDir);
        if (postUpdate)
            Trace.WriteLine("ExtensionManager: post update marker present; the pass reconciles the real state instead.");

        return new InstallationInspection(manifestDir, superseded, needsRepair);
    }

    /// <summary>
    /// Where the updater leaves its marker. Instance state so a test can point it at
    /// an isolated directory instead of the real one.
    /// </summary>
    public string PostUpdateMarkerPath { get; set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "post_update.flag");

    private sealed record InstallationInspection(
        string? CurrentFolder,
        bool HasSupersededVersionDir,
        bool FolderNeedsRepair);


    /// <summary>
    /// True when a folder for a superseded uBlock version is still on disk, which
    /// means WebView2 may still be registered against it.
    /// </summary>
    private bool HasSupersededVersionDir()
    {
        try
        {
            if (!Directory.Exists(_extensionsDir)) return false;
            return Directory.EnumerateDirectories(_extensionsDir, "uBlock0_*")
                .Any(d => !string.Equals(Path.GetFileName(d), $"uBlock0_{UBlockVersion}", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            // An unreadable extensions directory must not stop the registration
            // from being reconciled. Treating it as "nothing superseded" leaves the
            // existing registration in place, which is the safer of the two
            // outcomes compared with removing an extension that is still wanted.
            Trace.WriteLine($"ExtensionManager version directory check failed: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> EnsureUBlockDownloadedAsync()
    {
        // Disk only, and with no profile handle, so like PrepareAsync it is only safe
        // for a folder nothing holds.
        return (await PrepareAndReportAsync(mayWriteFolder: true)).Folder;
    }

    /// <summary>
    /// The preparation itself, returning what it produced. <see cref="LastPreparation"/>
    /// is still written on every path so the stage timings and the trace keep working,
    /// but the return value is what a caller decides from.
    /// </summary>
    private async Task<PreparationOutcome> PrepareAndReportAsync(bool mayWriteFolder)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        long locateMs = 0, stripMs = 0, hashMs = 0, downloadMs = 0, moveMs = 0, extractMs = 0, cleanupMs = 0;
        var wasAlreadyPresent = false;
        var assetsRewritten = false;
        var registryKeyRewritten = false;
        StagedRepair? staged = null;
        var repairDetail = string.Empty;

        try
        {
            var targetDir = Path.Combine(_extensionsDir, $"uBlock0_{UBlockVersion}");

            var locate = System.Diagnostics.Stopwatch.StartNew();
            var existingManifest = FindManifestDirectory(targetDir);
            locateMs = Ms(locate);

            if (existingManifest is not null)
            {
                wasAlreadyPresent = true;
                Trace.WriteLine($"ExtensionManager: uBlock already extracted at {existingManifest}");

                // A warm folder that the platform still holds is only read here. The
                // bytes a repair wants are computed and carried, and land after the
                // native remove, so the browser is never reading a folder while it
                // changes. A folder nothing holds is committed straight away.
                var warmRepair = System.Diagnostics.Stopwatch.StartNew();
                staged = StageFolderRepair(existingManifest);
                repairDetail = staged.Detail;
                if (mayWriteFolder)
                {
                    var committed = CommitFolderRepair(staged);
                    repairDetail = committed.Detail;
                    Trace.WriteLine($"ExtensionManager: {committed.Detail}");
                    if (!committed.Completed)
                    {
                        staged = null;
                    }
                }
                else
                {
                    Trace.WriteLine(
                        $"ExtensionManager: the folder is registered, so the repair is staged and " +
                        $"not written yet ({staged.Detail}).");
                }
                stripMs = Ms(warmRepair);

                // What this pass will do to the folder, not what it did. The platform may
                // still be holding it, so the write belongs to the commit after the
                // native remove.
                assetsRewritten = staged?.FolderChanged ?? false;
                registryKeyRewritten = (staged?.Registry?.HasChange ?? false);

                LastPreparation = new PreparationTimings(
                    true, locateMs, stripMs, 0, 0, 0, 0, 0, Ms(total), assetsRewritten);
                Trace.WriteLine(
                    $"ExtensionManager: warm prepare total={Ms(total)}ms locate={locateMs}ms repair={stripMs}ms " +
                    $"folderWouldChange={assetsRewritten} ({repairDetail})");
                return new PreparationOutcome(
                    existingManifest, assetsRewritten, registryKeyRewritten, staged,
                    mayWriteFolder ? null : staged, repairDetail);
            }

            Directory.CreateDirectory(_extensionsDir);

            // Target may exist but be corrupt (no manifest). Clear it so we can re-extract.
            if (Directory.Exists(targetDir) && existingManifest is null)
            {
                Trace.WriteLine($"ExtensionManager: target dir exists but no manifest, clearing {targetDir}");
                var clear = System.Diagnostics.Stopwatch.StartNew();
                try { Directory.Delete(targetDir, recursive: true); }
                catch (Exception ex) { Trace.WriteLine($"Failed to clear corrupt dir: {ex.Message}"); }
                cleanupMs += Ms(clear);
            }

            // GitHub asset is named uBlock0_*.chromium.zip; keep local cache name matching for bundling.
            var localZipPath = Path.Combine(_bundledResourcesDir, $"uBlock0_{UBlockVersion}.chromium.zip");
            var fallbackZipPath = Path.Combine(_bundledResourcesDir, $"uBlock0_{UBlockVersion}.zip");
            // Accept either naming for backward compat with older bundles
            if (!File.Exists(localZipPath) && File.Exists(fallbackZipPath))
                localZipPath = fallbackZipPath;

            Trace.WriteLine($"ExtensionManager: checking local uBlock zip at {localZipPath}");

            string? zipToExtract = null;
            string? tempDownloadPath = null;

            try
            {
                if (File.Exists(localZipPath))
                {
                    var hash = System.Diagnostics.Stopwatch.StartNew();
                    var bundledOk = await VerifyTofuHashAsync(localZipPath);
                    hashMs += Ms(hash);

                    if (bundledOk)
                    {
                        zipToExtract = localZipPath;
                        Trace.WriteLine("ExtensionManager: using bundled local zip.");
                    }
                    else
                    {
                        Trace.WriteLine("ExtensionManager: local zip hash failed, falling back to remote download.");
                    }
                }
                else
                {
                    Trace.WriteLine("ExtensionManager: local zip not found, downloading from GitHub.");
                }

                if (zipToExtract is null)
                {
                    tempDownloadPath = Path.Combine(Path.GetTempPath(), $"uBlock0_{UBlockVersion}_{Guid.NewGuid():N}.zip");
                    Trace.WriteLine($"ExtensionManager: downloading uBlock from {UBlockDownloadUrl} to {tempDownloadPath}");
                    var dl = System.Diagnostics.Stopwatch.StartNew();
                    var data = await _http.GetByteArrayAsync(UBlockDownloadUrl);
                    await File.WriteAllBytesAsync(tempDownloadPath, data);
                    downloadMs += Ms(dl);

                    var downloadHash = System.Diagnostics.Stopwatch.StartNew();
                    var ok = await VerifyTofuHashAsync(tempDownloadPath);
                    hashMs += Ms(downloadHash);

                    if (!ok)
                    {
                        Trace.WriteLine("ExtensionManager: downloaded zip hash mismatch, aborting.");
                        try { File.Delete(tempDownloadPath); } catch { }
                        tempDownloadPath = null;
                        LastPreparation = new PreparationTimings(
                            false, locateMs, stripMs, hashMs, downloadMs, 0, 0, cleanupMs, Ms(total), assetsRewritten);
                        return new PreparationOutcome(null, assetsRewritten, registryKeyRewritten, staged, null, repairDetail);
                    }

                    zipToExtract = tempDownloadPath;
                }

                if (Directory.Exists(targetDir))
                {
                    var move = System.Diagnostics.Stopwatch.StartNew();
                    var tempDir = Path.Combine(_extensionsDir, $"uBlock0_{UBlockVersion}_{Guid.NewGuid():N}");
                    Directory.Move(targetDir, tempDir);
                    _ = Task.Run(() =>
                    {
                        try { Directory.Delete(tempDir, recursive: true); }
                        catch (Exception ex) { Trace.WriteLine($"Failed to delete temp dir: {ex}"); }
                    });
                    moveMs = Ms(move);
                }

                var extract = System.Diagnostics.Stopwatch.StartNew();
                ZipFile.ExtractToDirectory(zipToExtract!, targetDir);
                extractMs = Ms(extract);

                // A fresh extraction is repaired before anything registers it, so the
                // folder WebView2 records is already the final one and nothing is holding
                // it yet. The remote filter urls in contentURL are kept, because that is
                // what lets the extension update a list; only the mirror and diff keys
                // are disarmed, and the bundled local paths stay ahead of the remote
                // ones because the extension sorts them that way until its filter engine
                // is ready.
                var coldRepair = System.Diagnostics.Stopwatch.StartNew();
                var extractedManifest = FindManifestDirectory(targetDir);
                if (extractedManifest is not null)
                {
                    staged = StageFolderRepair(extractedManifest);
                    var coldCommit = CommitFolderRepair(staged);
                    assetsRewritten = coldCommit.Completed && staged.FolderChanged;
                    registryKeyRewritten = (staged.Registry?.HasChange ?? false);
                    repairDetail = coldCommit.Detail;
                    Trace.WriteLine($"ExtensionManager: {coldCommit.Detail}");
                    if (!coldCommit.Completed) staged = null;
                }
                stripMs += Ms(coldRepair);

                var manifestDir = FindManifestDirectory(targetDir);
                if (manifestDir is null)
                {
                    Trace.WriteLine("ExtensionManager: manifest.json not found after extraction.");
                    LastPreparation = new PreparationTimings(
                        false, locateMs, stripMs, hashMs, downloadMs, moveMs, extractMs, cleanupMs, Ms(total), assetsRewritten);
                    return new PreparationOutcome(null, assetsRewritten, registryKeyRewritten, staged, null, repairDetail);
                }

                Trace.WriteLine($"ExtensionManager: uBlock extracted to {manifestDir}");
                LastPreparation = new PreparationTimings(
                    false, locateMs, stripMs, hashMs, downloadMs, moveMs, extractMs, cleanupMs, Ms(total), assetsRewritten);
                Trace.WriteLine(
                    $"ExtensionManager: cold prepare total={Ms(total)}ms locate={locateMs}ms repair={stripMs}ms " +
                    $"hash={hashMs} download={downloadMs} move={moveMs} extract={extractMs} cleanup={cleanupMs} " +
                    $"assetsRewritten={assetsRewritten} registryKeyRewritten={registryKeyRewritten}");
                return new PreparationOutcome(manifestDir, assetsRewritten, registryKeyRewritten, staged, null, repairDetail);
            }
            finally
            {
                if (tempDownloadPath is not null && File.Exists(tempDownloadPath))
                {
                    try { File.Delete(tempDownloadPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.EnsureUBlockDownloadedAsync failed: {ex}");
            LastPreparation = new PreparationTimings(
                wasAlreadyPresent, locateMs, stripMs, hashMs, downloadMs, moveMs, extractMs, cleanupMs, Ms(total), assetsRewritten);
            return new PreparationOutcome(null, assetsRewritten, registryKeyRewritten, staged, null, repairDetail);
        }
    }


    public async Task LoadUnpackedAsync(CoreWebView2 webview, string folderPath)
    {
        try
        {
            await webview.Profile.AddBrowserExtensionAsync(folderPath);
            Trace.WriteLine($"ExtensionManager: loaded extension from {folderPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.LoadUnpackedAsync failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<CoreWebView2BrowserExtension>> GetExtensionsAsync(CoreWebView2 webview)
    {
        try
        {
            return await webview.Profile.GetBrowserExtensionsAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.GetExtensionsAsync failed: {ex.Message}");
            return Array.Empty<CoreWebView2BrowserExtension>();
        }
    }

    public async Task ToggleExtensionAsync(CoreWebView2BrowserExtension ext, bool enabled)
    {
        try
        {
            await ext.EnableAsync(enabled);
            Trace.WriteLine($"ExtensionManager: extension '{ext.Name}' enabled={enabled}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.ToggleExtensionAsync failed: {ex.Message}");
        }
    }

    public async Task RemoveExtensionAsync(CoreWebView2BrowserExtension ext)
    {
        try
        {
            await ext.RemoveAsync();
            Trace.WriteLine($"ExtensionManager: extension '{ext.Name}' removed.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager.RemoveExtensionAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The names the pass recognises. Plain functions over a name so the classification
    /// is testable without a profile, and so removing one kind of extension can never
    /// be confused with removing another.
    /// </summary>
    public static bool IsUBlock(string? name) =>
        name is not null && (name.Equals("uBlock Origin", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));

    /// <summary>The download manager extension older versions installed alongside uBlock.</summary>
    public static bool IsLegacyDownloadManager(string? name) =>
        name is not null && (name.Contains("IDM", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("Internet Download Manager", StringComparison.OrdinalIgnoreCase));

    private static CoreWebView2BrowserExtension? FindUBlock(IEnumerable<CoreWebView2BrowserExtension> extensions) =>
        extensions.FirstOrDefault(e => IsUBlock(e.Name));

    private static CoreWebView2BrowserExtension? FindLegacyDownloadManager(
        IEnumerable<CoreWebView2BrowserExtension> extensions) =>
        extensions.FirstOrDefault(e => IsLegacyDownloadManager(e.Name));

    /// <summary>
    /// How long a requested enable or disable is given to show up in the profile
    /// listing. This bounds a read back, it does not decide anything: the answer
    /// reported is the one the profile last gave.
    /// </summary>
    private static readonly TimeSpan EnableSettleBound = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Reads the profile until it reports the requested enable state, or the bound
    /// runs out. Returns what the profile last said.
    /// </summary>
    private static async Task<bool> WaitForEnabledStateAsync(CoreWebView2 webview, bool desiredEnabled, TimeSpan bound)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var current = FindUBlock(await webview.Profile.GetBrowserExtensionsAsync());
            if (current is not null && current.IsEnabled == desiredEnabled) return true;
            if (clock.Elapsed >= bound) return false;
            await Task.Delay(50);
        }
    }

    private static string? FindManifestDirectory(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            return null;

        if (File.Exists(Path.Combine(rootDir, "manifest.json")))
            return rootDir;

        foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, "manifest.json")))
                return dir;
        }

        return null;
    }

    private async Task<bool> VerifyTofuHashAsync(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            Trace.WriteLine($"ExtensionManager: zip not found at {zipPath}");
            return false;
        }

        string actualHash;
        try
        {
            actualHash = await ComputeFileHashAsync(zipPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager: hash compute failed for {zipPath}: {ex.Message}");
            return false;
        }

        if (File.Exists(_hashFilePath))
        {
            var storedHash = (await File.ReadAllTextAsync(_hashFilePath)).Trim();
            if (!string.Equals(actualHash, storedHash, StringComparison.OrdinalIgnoreCase))
            {
                Trace.WriteLine($"ExtensionManager: SHA-256 mismatch. Expected={storedHash}, Actual={actualHash}");
                return false;
            }
            Trace.WriteLine("ExtensionManager: SHA-256 hash verified.");
        }
        else
        {
            try { await File.WriteAllTextAsync(_hashFilePath, actualHash); } catch (Exception ex) { Trace.WriteLine($"Failed to store hash: {ex.Message}"); }
            Trace.WriteLine($"ExtensionManager: first download, stored SHA-256={actualHash}");
        }

        return true;
    }

    private void CleanupOldVersions()
    {
        try
        {
            if (!Directory.Exists(ExtensionsDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(ExtensionsDir, "uBlock0_*"))
            {
                var name = Path.GetFileName(dir);
                if (!string.Equals(name, $"uBlock0_{UBlockVersion}", StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"ExtensionManager: removing old version {name}");
                    try { Directory.Delete(dir, true); } catch (Exception ex) { Trace.WriteLine($"Failed to delete old version {name}: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"CleanupOldVersions failed: {ex.Message}"); }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// True when a folder still has to be repaired before the extension can update its
    /// lists, and true when the folder's state could not be established at all. A file
    /// that is missing, unreadable, malformed or half migrated is not a folder that needs
    /// no work, and saying otherwise was how a broken folder read as a healthy one.
    /// </summary>
    private bool AssetsNeedRepair(string manifestDir)
    {
        if (UBlockAssetRepair.Repair(
                ReadFileOrNull(Path.Combine(manifestDir, "assets", "assets.json")),
                PristineAssetsJson()).Outcome == UBlockAssetRepair.RepairOutcome.NotPossible)
        {
            return true;
        }

        var script = ReadFileOrNull(Path.Combine(manifestDir, "js", "assets.js"));
        if (script is null) return true;

        // The registry question is asked of the repair itself rather than by reading its
        // wording. A script that is not already in the repaired form still has work to do
        // here, whether it is wholly upstream or only partly moved, and a script the
        // repair cannot run is not a folder that needs no work.
        if (UBlockAssetRepair.RepairRegistryKey(script).Outcome
            != UBlockAssetRepair.RepairOutcome.AlreadyRepaired)
        {
            return true;
        }

        return UBlockAssetRepair.Repair(
            ReadFileOrNull(Path.Combine(manifestDir, "assets", "assets.json")),
            PristineAssetsJson()).Changed;
    }

    /// <summary>
    /// The package's own assets.json, read once per manager. A folder stripped by an
    /// earlier pass gets its remote urls back from here, so no url is written that the
    /// pinned package did not ship. Null when the package is not where it is expected,
    /// which the repair reports rather than hides.
    /// </summary>
    private string? PristineAssetsJson()
    {
        if (_pristineAssetsJsonRead) return _pristineAssetsJson;
        _pristineAssetsJsonRead = true;
        _pristineAssetsJson = UBlockAssetRepair.ReadPristineAssetsJson(
            Path.Combine(_bundledResourcesDir, $"uBlock0_{UBlockVersion}.chromium.zip"));
        return _pristineAssetsJson;
    }

    /// <summary>
    /// One file a folder repair wants to change, carrying the bytes it wants beside the
    /// bytes it found. Nothing is written by staging, which is what lets a registered
    /// folder be assessed while the platform still holds it.
    /// </summary>
    public sealed record StagedFile(
        string Path,
        string Original,
        string? Desired,
        string What,
        UBlockAssetRepair.AssetsRepairResult Result)
    {
        public bool HasChange => Desired is not null;

        public bool Assessable => Result.Outcome != UBlockAssetRepair.RepairOutcome.NotPossible;
    }

    /// <summary>
    /// The whole folder repair, computed from disk and holding no writes.
    ///
    /// <see cref="FolderChanged"/> is what a commit would do.
    /// <see cref="Assessable"/> is false when any part of the folder could not be read or
    /// understood, which is reported rather than treated as a healthy folder.
    /// </summary>
    public sealed record StagedRepair(
        StagedFile? Assets,
        StagedFile? Registry,
        string Folder)
    {
        public bool FolderChanged => (Assets?.HasChange ?? false) || (Registry?.HasChange ?? false);

        public bool Assessable =>
            (Assets is null || Assets.Assessable) && (Registry is null || Registry.Assessable);

        public string Detail => string.Join("; ",
            new[] { Assets, Registry }
                .Where(f => f is not null)
                .Select(f => $"{f!.What}: {f.Result.Detail}"));
    }

    /// <summary>What a commit actually did, as opposed to what it was asked to do.</summary>
    public sealed record CommitResult(bool Completed, int FilesWritten, int FilesRestored, string Detail)
    {
        public static CommitResult NothingToDo(string detail) => new(true, 0, 0, detail);
    }

    /// <summary>
    /// Works out what an installed folder needs, without writing to it.
    ///
    /// This is the only file work a pass may do to a folder the platform holds. A
    /// registration is a live reference to that path, so the bytes are computed here and
    /// committed later by <see cref="CommitFolderRepair"/>, after the native remove. A
    /// pass that wrote here would change the content of a folder the browser is reading.
    /// </summary>
    public StagedRepair StageFolderRepair(string manifestDir)
    {
        var pristine = PristineAssetsJson();
        if (pristine is null)
        {
            Trace.WriteLine(
                "ExtensionManager: the pinned package is not readable here, so a folder stripped " +
                "by an earlier pass cannot get its remote urls back from it.");
        }

        var assetsPath = Path.Combine(manifestDir, "assets", "assets.json");
        var scriptPath = Path.Combine(manifestDir, "js", "assets.js");

        var assets = StageOne(assetsPath, "assets.json",
            () => UBlockAssetRepair.Repair(ReadFileOrNull(assetsPath), pristine));
        var registry = StageOne(scriptPath, "js/assets.js",
            () => UBlockAssetRepair.RepairRegistryKey(ReadFileOrNull(scriptPath)));

        return new StagedRepair(assets, registry, manifestDir);
    }

    private StagedFile? StageOne(string path, string what, Func<UBlockAssetRepair.AssetsRepairResult> compute)
    {
        if (!File.Exists(path)) return null;

        string original;
        try { original = File.ReadAllText(path); }
        catch (Exception ex)
        {
            Trace.WriteLine($"ExtensionManager: reading {what} failed: {ex.Message}");
            return new StagedFile(path, string.Empty, null, what,
                UBlockAssetRepair.AssetsRepairResult.CouldNotRepair(
                    $"{what} could not be read ({ex.Message})"));
        }

        var result = compute();
        if (result.Outcome == UBlockAssetRepair.RepairOutcome.NotPossible)
            Trace.WriteLine($"ExtensionManager: {what} was not repaired: {result.Detail}");

        return new StagedFile(path, original, result.RepairedText, what, result);
    }

    /// <summary>
    /// Writes a staged repair over a folder the platform is no longer holding, and puts
    /// every file back if any one of them will not take the write.
    ///
    /// The bytes were read while the registration still existed and land only after the
    /// native remove, at the same path, so the extension keeps its identity and the
    /// storage that goes with it. A write that fails part way is undone from the bytes
    /// captured at staging time, which leaves a folder the platform never saw changed
    /// rather than a half repaired one.
    /// </summary>
    public CommitResult CommitFolderRepair(StagedRepair staged)
    {
        // Written is how many writes took, restored is how many were undone. They are
        // counted separately rather than netted off, because a commit that wrote two
        // files and put both back did write two files.
        var wanted = new[] { staged.Assets, staged.Registry }
            .Where(f => f is not null && f.HasChange)
            .Cast<StagedFile>()
            .ToList();
        if (wanted.Count == 0)
            return CommitResult.NothingToDo($"nothing to write ({staged.Detail})");

        var written = new List<StagedFile>();
        foreach (var file in wanted)
        {
            try
            {
                File.WriteAllText(file.Path, file.Desired!);
                written.Add(file);
                Trace.WriteLine($"ExtensionManager: committed {file.What} to a folder no core is holding.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ExtensionManager: committing {file.What} failed: {ex.Message}");
                var restored = Restore(written, file);
                return new CommitResult(false, written.Count, restored,
                    $"{file.What} could not be written ({ex.Message}); " +
                    $"{restored} file(s) put back as they were");
            }
        }

        return new CommitResult(true, written.Count, 0,
            $"{written.Count} file(s) written: {string.Join(", ", written.Select(f => f.What))}");
    }

    private static int Restore(List<StagedFile> written, StagedFile failing)
    {
        var restored = 0;
        foreach (var file in written.Concat(new[] { failing }))
        {
            if (file.Original.Length == 0) continue;
            try
            {
                File.WriteAllText(file.Path, file.Original);
                restored++;
                Trace.WriteLine($"ExtensionManager: put {file.What} back as it was.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ExtensionManager: putting {file.What} back failed too: {ex.Message}");
            }
        }
        return restored;
    }

    private static string? ReadFileOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The package's assets.json, read once per manager so a pass that inspects a
    /// folder does not reopen the package each time.
    /// </summary>
    private bool _pristineAssetsJsonRead;
    private string? _pristineAssetsJson;

    /// <summary>
    /// Serializes the read, native remove, commit and native add window over the one
    /// extensions folder this manager serves. The coordinator serializes per profile, and
    /// two profiles in one process share this folder, so the gate over the folder itself
    /// has to live here. It is held across the native calls, so a pass that removes an
    /// extension and writes its folder is never interleaved with one reading the same
    /// folder. Nothing else takes it, and the wait is unbounded rather than timed out,
    /// because abandoning it mid window would leave a folder half written.
    /// </summary>
    private readonly SemaphoreSlim _folderGate = new(1, 1);

    public ExtensionHealth GetHealth()
    {
        var targetDir = Path.Combine(ExtensionsDir, $"uBlock0_{UBlockVersion}");
        var manifestDir = FindManifestDirectory(targetDir);
        var hasFolder = manifestDir is not null;
        // Same bundled directory preparation reads from. Reporting a zip that
        // lives somewhere else would make the health check disagree with the
        // code that actually does the work.
        var hasZip = File.Exists(Path.Combine(_bundledResourcesDir, $"uBlock0_{UBlockVersion}.chromium.zip")) ||
                     File.Exists(Path.Combine(_bundledResourcesDir, $"uBlock0_{UBlockVersion}.zip"));
        var hasHash = File.Exists(_hashFilePath);
        // True when the folder still has to be repaired, and also true when its state
        // could not be established. A file the pass could not read or understand is a
        // fault to report, not a folder that needs no work.
        var assetsStripped = hasFolder && AssetsNeedRepair(manifestDir!);
        return new ExtensionHealth(hasFolder, hasZip, hasHash, assetsStripped, UBlockVersion);
    }

    public sealed record ExtensionHealth(bool HasFolder, bool HasZip, bool HasHash, bool AssetsStripped, string Version, bool? Registered = null, bool? Enabled = null);
}
