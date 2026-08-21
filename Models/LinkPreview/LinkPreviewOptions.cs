namespace StrideBrowser.Models.LinkPreview;

/// <summary>
/// User-controllable link preview options. Persisted via ISettingsStore.
/// Alt held plus hover or Alt held plus press are the only triggers. No long press.
/// </summary>
public sealed record LinkPreviewOptions(
    bool Enabled,
    string Hotkey,
    bool AllowPress
)
{
    public static LinkPreviewOptions Default => new(
        Enabled: true,
        Hotkey: "Alt",
        AllowPress: true
    );
}
