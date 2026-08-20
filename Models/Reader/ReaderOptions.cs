namespace StrideBrowser.Models.Reader;

/// <summary>
/// User-controllable reader presentation. Persisted via <see cref="Services.ISettingsStore"/>.
/// </summary>
public sealed record ReaderOptions(
    double FontScale,
    string Theme,
    double ContentWidth
)
{
    public static ReaderOptions Default => new(
        FontScale: 1.0,
        Theme: "system",
        ContentWidth: 720
    );
}
