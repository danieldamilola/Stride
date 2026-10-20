using System;
using StrideBrowser.Models;

namespace StrideBrowser.Engine;

/// <summary>
/// Enforces route permissions for privileged browser commands originating from internal documents.
/// Pure logic covered by unit tests.
///
/// Each arm below is the exact set of routes its page emits, matched with the same
/// contract the router uses: Prefix routes with StartsWith, everything else with equality.
/// The source, identity and token gates in WebViewIpcBridge run before this and are not
/// affected by anything here.
/// </summary>
public static class IpcSecurityPolicy
{
    /// <summary>
    /// Onboarding.html posts these two as bare literals and OnboardingMessageHandler.cs:24-25
    /// routes them by exact match. They have no WebMessagePrefix constant of their own.
    /// onboarding-reset is routed too but no page sends it, so it stays denied here.
    /// </summary>
    private const string OnboardingDone = "onboarding-done";
    private const string OnboardingReady = "onboarding-ready";

    public static bool IsRouteAllowedForPage(TabEngine.InternalPageKind pageKind, string message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        return pageKind switch
        {
            TabEngine.InternalPageKind.Settings =>
                message.StartsWith(WebMessagePrefix.Setting, StringComparison.Ordinal) ||
                message.StartsWith("shortcut-recording:", StringComparison.Ordinal) ||
                message == WebMessagePrefix.ResetSettings ||
                message == WebMessagePrefix.CheckForUpdate ||
                message.StartsWith(WebMessagePrefix.InstallUpdate, StringComparison.Ordinal) ||
                message == WebMessagePrefix.OpenBackgroundsFolder ||
                message == WebMessagePrefix.OpenReleaseNotes ||
                message == WebMessagePrefix.OpenOnboarding ||
                // The About card posts this, and CoreMessageHandler.cs:27 routes it by
                // exact match. Without it the Set Default button reaches nothing.
                message == WebMessagePrefix.SetDefaultBrowser,

            TabEngine.InternalPageKind.History =>
                message.StartsWith(WebMessagePrefix.HistoryOpen, StringComparison.Ordinal) ||
                message == WebMessagePrefix.HistoryClear,

            TabEngine.InternalPageKind.OneTab =>
                message.StartsWith(WebMessagePrefix.OneTabRestore, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabDelete, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabRename, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabOpen, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabDeleteTab, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabStar, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabReorderTab, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.OneTabReorderGroup, StringComparison.Ordinal),

            TabEngine.InternalPageKind.Downloads =>
                message.StartsWith(WebMessagePrefix.DownloadOpen, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.DownloadFolder, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.DownloadCancel, StringComparison.Ordinal) ||
                message == WebMessagePrefix.DownloadClear ||
                message == WebMessagePrefix.DownloadClearAll ||
                message == WebMessagePrefix.DownloadRequestSync ||
                message.StartsWith(WebMessagePrefix.DownloadPause, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.DownloadResume, StringComparison.Ordinal),

            TabEngine.InternalPageKind.NewTab =>
                message.StartsWith(WebMessagePrefix.Open, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.Search, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.ShortcutAdd, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.ShortcutRemove, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.ShortcutClick, StringComparison.Ordinal) ||
                message == WebMessagePrefix.FocusSettings ||
                message.StartsWith("setting:newTabBackground:", StringComparison.Ordinal) ||
                message == WebMessagePrefix.OpenBackgroundsFolder,

            TabEngine.InternalPageKind.Onboarding =>
                message == WebMessagePrefix.WhatsNewContinue ||
                message == WebMessagePrefix.WhatsNewDismiss ||
                message == WebMessagePrefix.SetDefaultBrowser ||
                message.StartsWith(WebMessagePrefix.Open, StringComparison.Ordinal) ||
                message.StartsWith(WebMessagePrefix.Search, StringComparison.Ordinal) ||
                // The Done button and the ready handshake. Without these a first run
                // cannot finish and the settings snapshot never comes back.
                message == OnboardingDone ||
                message == OnboardingReady ||
                // First run writes preferences through the same setting route the
                // Settings page uses: theme, accent, search engine, ad blocking,
                // SmartScreen, force HTTPS, clear on exit, hibernation, tab names.
                message.StartsWith(WebMessagePrefix.Setting, StringComparison.Ordinal),

            TabEngine.InternalPageKind.ReleaseNotes =>
                message.StartsWith(WebMessagePrefix.Open, StringComparison.Ordinal) ||
                // The What is New page's own two buttons. OnboardingMessageHandler.cs:27-28
                // routes them by exact match. Both close or reroute that tab.
                message == WebMessagePrefix.WhatsNewContinue ||
                message == WebMessagePrefix.WhatsNewDismiss,

            TabEngine.InternalPageKind.Focus =>
                message == WebMessagePrefix.FocusSettings ||
                message.StartsWith(WebMessagePrefix.Open, StringComparison.Ordinal),

            _ => false
        };
    }

    public static bool IsRouteAllowedForLocalAsset(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.StartsWith(WebMessagePrefix.TCLensGetText, StringComparison.Ordinal);
    }
}
