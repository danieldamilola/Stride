namespace UmberBrowser.Models;

using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>Kinds of address bar suggestions, driving the row icon.</summary>
public enum SuggestionKind
{
    History,
    Shortcut,
    Search
}

/// <summary>
/// One address bar suggestion row. DisplayText fills the box and renders as
/// the primary row text. Url is the navigation target; null for web phrases,
/// which resolve through search instead. Favicon fills in asynchronously once
/// the loader resolves the host icon.
/// </summary>
public sealed partial class SuggestionItem : ObservableObject
{
    public required string DisplayText { get; init; }

    public string? Url { get; init; }

    public string Suffix { get; init; } = "";

    public SuggestionKind Kind { get; init; }

    [ObservableProperty]
    private ImageSource? _favicon;

    public string NavigateTarget => Url ?? DisplayText;
}
