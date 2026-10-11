namespace StrideBrowser.Services.UI;

public sealed class BrowserWindowContext
{
    public bool RestoreStartupSession { get; set; }
    public bool IsClosing { get; set; }
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
