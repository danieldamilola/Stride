using CommunityToolkit.Mvvm.ComponentModel;
using SpurBrowser.Models;
using SpurBrowser.Services;

namespace SpurBrowser.ViewModels;

/// <summary>
/// Thin coordinator between the UI and the engine/services.
/// Contains NO business logic — only UI state and command routing.
/// </summary>
public sealed partial class BrowserViewModel : ObservableObject
{
    private readonly NavigationService _navigation;

    public BrowserSettings Settings { get; }

    [ObservableProperty]
    private string _addressText = "";

    /// <summary>True while the active tab is navigating — drives the address bar loading cues.</summary>
    [ObservableProperty]
    private bool _isLoading;

    public BrowserViewModel(BrowserSettings settings, NavigationService navigation)
    {
        Settings = settings;
        _navigation = navigation;
    }

    /// <summary>Resolves a raw input string into a URL.</summary>
    public string ResolveInput(string input) => _navigation.Resolve(input);

    /// <summary>Updates the address bar from the active tab's state.</summary>
    public void SyncAddressBar(BrowserTab tab)
    {
        AddressText = InternalUrls.IsInternal(tab.Url) ? "" : tab.Url;
    }
}
