namespace StrideBrowser.Services.UI;

/// <summary>
/// The per-window record. A window is built before it has loaded, so anything
/// that docks a tab into it has to wait for <see cref="Ready"/>, and a window
/// that closes before then must never wait forever.
/// </summary>
public sealed class BrowserWindowContext
{
    /// <summary>True only for the window that starts the process and owns command line args.</summary>
    public bool RestoreStartupSession { get; set; }

    /// <summary>Set by the window manager before the window is torn down.</summary>
    public bool IsClosing { get; set; }

    /// <summary>Completes when the window has restored its tabs and built its startup bindings.</summary>
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}