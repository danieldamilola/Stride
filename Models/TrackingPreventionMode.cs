using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser.Models;

/// <summary>
/// How hard WebView2 tracking prevention works. These are the only two levels Stride
/// offers, and both keep protection switched on.
/// </summary>
/// <remarks>
/// The engine also has None and Basic, which turn tracking prevention off or leave only
/// malicious-tracker blocking. A browser does not need a control that removes its own
/// protection, so neither is offered here. Microsoft documents that Strict "blocks the
/// most trackers but could cause some websites to not behave as expected", which is what
/// the settings page tells the user before they pick it.
/// </remarks>
public enum TrackingPreventionMode
{
    Balanced,
    Strict
}

/// <summary>Parsing and WebView2 mapping for <see cref="TrackingPreventionMode"/>.</summary>
public static class TrackingPreventionModes
{
    /// <summary>The level a profile runs at unless the user picks Strict.</summary>
    public const TrackingPreventionMode Default = TrackingPreventionMode.Balanced;

    /// <summary>
    /// Reads a value that came from the settings page or from settings.json. Anything
    /// that is not a known mode falls back to the default rather than failing, so one
    /// bad value cannot take the rest of the preferences down with it.
    /// </summary>
    public static TrackingPreventionMode Parse(string? value)
    {
        if (Enum.TryParse<TrackingPreventionMode>(value?.Trim(), ignoreCase: true, out var mode)
            && Enum.IsDefined(mode))
            return mode;

        return Default;
    }

    /// <summary>
    /// The engine level for a mode. Both branches are explicit rather than a cast,
    /// because the engine enum has four members and only two are reachable from here.
    /// </summary>
    public static CoreWebView2TrackingPreventionLevel ToWebView2Level(TrackingPreventionMode mode) =>
        mode == TrackingPreventionMode.Strict
            ? CoreWebView2TrackingPreventionLevel.Strict
            : CoreWebView2TrackingPreventionLevel.Balanced;
}

/// <summary>
/// Reads and writes <see cref="TrackingPreventionMode"/> as its name, and falls back to
/// Balanced for anything it does not recognise.
/// </summary>
/// <remarks>
/// Without this, System.Text.Json throws on a hand-edited or truncated value. That throw
/// happens while the whole settings object is being read, so a single bad token would
/// discard every other preference the user had. One tolerant converter keeps the blast
/// radius to the one field.
/// </remarks>
public sealed class TrackingPreventionModeJsonConverter : JsonConverter<TrackingPreventionMode>
{
    public override TrackingPreventionMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return TrackingPreventionModes.Parse(reader.GetString());
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return Enum.IsDefined(typeof(TrackingPreventionMode), number)
                    ? (TrackingPreventionMode)number
                    : TrackingPreventionModes.Default;
            default:
                reader.Skip();
                return TrackingPreventionModes.Default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TrackingPreventionMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}