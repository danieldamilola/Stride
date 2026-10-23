using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrideBrowser.Models;
using StrideBrowser.Services;
using StrideBrowser.Services.Downloads;

using StrideBrowser.Services.Reader;
using StrideBrowser.ViewModels.Reader;

namespace StrideBrowser.ViewModels;

/// <summary>
/// Thin coordinator between the UI and the engine/services.
/// Contains NO business logic - only UI state and command routing.
/// </summary>
public sealed partial class BrowserViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _suggestionCts;
    private readonly NavigationService _navigation;
    private readonly ISuggestionProvider _suggestionProvider;
    private readonly FaviconLoader _faviconLoader;

    public BrowserSettings Settings { get; }

    [ObservableProperty]
    private string _addressText = "";

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<SuggestionItem> _suggestions = new();

    [ObservableProperty]
    private int _selectedSuggestionIndex = -1;

    [ObservableProperty]
    private bool _showSuggestions;

    /// <summary>
    /// Drives the toolbar suggestion popup. The popup binds two way and
    /// writes back false on auto close, which forwards to ShowSuggestions.
    /// </summary>
    public bool StandardPopupOpen
    {
        get => ShowSuggestions;
        set { if (!value && ShowSuggestions) ShowSuggestions = false; }
    }

    partial void OnShowSuggestionsChanged(bool value) => OnPropertyChanged(nameof(StandardPopupOpen));

    /// <summary>Set while the box text comes from a programmatic write, not typing.</summary>
    public bool SuppressSuggestionRefresh { get; set; }

    /// <summary>True while a suggestion row is selected.</summary>
    public bool IsRowHighlighted => SelectedSuggestionIndex >= 0;

    /// <summary>True after arrow navigation puts a suggestion's text in the address box.</summary>
    public bool IsSuggestionTextDisplayed { get; private set; }

    private string _typedQuery = "";

    /// <summary>True while the active tab is navigating - drives the address bar loading cues.</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    private double _activeDownloadsProgress;

    [ObservableProperty]
    private bool _hasIndeterminateDownloads;

    private UpdateState _updateState = UpdateState.None;

    public UpdateState UpdateState
    {
        get => _updateState;
        set
        {
            if (SetProperty(ref _updateState, value))
            {
                OnPropertyChanged(nameof(ShowUpdateIcon));
                OnPropertyChanged(nameof(ShowUpdateRedDot));
                OnPropertyChanged(nameof(ShowUpdateProgress));
                OnPropertyChanged(nameof(ShowUpdateCheckmark));
            }
        }
    }

    public bool ShowUpdateIcon => UpdateState != UpdateState.None;
    public bool ShowUpdateRedDot => UpdateState == UpdateState.UpdateAvailable;
    public bool ShowUpdateProgress => UpdateState == UpdateState.Downloading;
    public bool ShowUpdateCheckmark => UpdateState == UpdateState.ReadyToInstall;

    [ObservableProperty]
    private double _updateProgress = 0.0;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    /// <summary>Fallback tab favicon that follows the app theme. Dark by default.</summary>
    [ObservableProperty]
    private string _fallbackIconUri = "pack://application:,,,/icons/stride-browser-dark-32x32.png";

    private readonly Engine.TabEngine _engine;
    private readonly IReaderService _readerService;
    private readonly ReaderViewModel _readerViewModel;
    private readonly UpdateService _updateService;
    private bool _isInstalling;
    private Action? _unsubscribe;

    public BrowserViewModel(BrowserSettings settings, NavigationService navigation, IDownloadStore downloadStore, Engine.TabEngine engine, IReaderService readerService, ReaderViewModel readerViewModel, UpdateService updateService, ISuggestionProvider suggestions, FaviconLoader faviconLoader)
    {
        Settings = settings;
        _navigation = navigation;
        _suggestionProvider = suggestions;
        _faviconLoader = faviconLoader;
        _engine = engine;
        _readerService = readerService;
        _readerViewModel = readerViewModel;
        _updateService = updateService;

        EventHandler onUpdateAvailable = (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                UpdateState = UpdateState.UpdateAvailable;
                UpdateVersion = _updateService.LatestVersion ?? "";
            });
        };
        _updateService.UpdateAvailable += onUpdateAvailable;

        EventHandler<double> onDownloadProgressChanged = (s, progress) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                UpdateState = UpdateState.Downloading;
                UpdateProgress = progress / 100.0;
            });
        };
        _updateService.DownloadProgressChanged += onDownloadProgressChanged;

        EventHandler onDownloadCompleted = (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _isInstalling = false;
                UpdateState = UpdateState.ReadyToInstall;
            });
        };
        _updateService.DownloadCompleted += onDownloadCompleted;

        EventHandler<string> onUpdateFailed = (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _isInstalling = false;
                UpdateState = UpdateState.UpdateAvailable;
                UpdateProgress = 0;
            });
        };
        _updateService.UpdateFailed += onUpdateFailed;

        var downloadActivity = new DownloadActivityTracker(downloadStore.Items);
        void UpdateDownloads() => UpdateDownloadProgress(downloadActivity.ActiveItems);
        downloadActivity.Changed += UpdateDownloads;
        UpdateDownloads();

        // One view model per window, but the update service and the download store are
        // singletons that outlive every window. Without this a closed window would keep
        // its handlers alive and keep writing into a view model nothing is showing.
        _unsubscribe = () =>
        {
            _updateService.UpdateAvailable -= onUpdateAvailable;
            _updateService.DownloadProgressChanged -= onDownloadProgressChanged;
            _updateService.DownloadCompleted -= onDownloadCompleted;
            _updateService.UpdateFailed -= onUpdateFailed;
            downloadActivity.Changed -= UpdateDownloads;
            downloadActivity.Dispose();
        };
    }

    public void Dispose()
    {
        _unsubscribe?.Invoke();
        _unsubscribe = null;
        _suggestionCts?.Cancel();
    }

    private void UpdateDownloadProgress(IEnumerable<DownloadItem> items)
    {
        var active = items.Where(item => item.State == DownloadState.InProgress || item.IsStarting).ToArray();
        HasActiveDownloads = active.Length > 0;
        HasIndeterminateDownloads = active.Any(item => item.TotalBytes <= 0 || item.IsStarting);
        var total = active.Sum(item => (double)Math.Max(0, item.TotalBytes));
        ActiveDownloadsProgress = total > 0 && !HasIndeterminateDownloads
            ? Math.Clamp(active.Sum(item => (double)Math.Max(0, item.ReceivedBytes)) / total, 0, 1) : 0;
    }

    /// <summary>Resolves a raw input string into a URL.</summary>
    public string ResolveInput(string input) => _navigation.Resolve(input);

    /// <summary>Updates the address bar from the active tab's state.</summary>
    public void SyncAddressBar(BrowserTab tab)
    {
        SyncNavigationState();
        CurrentPageUrl = tab.Url;
        AddressText = NavigationService.ToDisplayText(tab.Url, tab.Title);
        NoteAddressDisplayed(AddressText);
    }

    public void SyncNavigationState()
    {
        IsLoading = _engine.ActiveTab?.IsLoading ?? false;
        var core = _engine.GetCoreWebView2();
        CanGoBack = core?.CanGoBack ?? false;
        CanGoForward = core?.CanGoForward ?? false;
    }

    /// <summary>Full URL of the active page. The box shows its domain until focused.</summary>
    public string CurrentPageUrl { get; private set; } = "";

    private bool _boxEditedSinceDisplay;
    private string _lastDisplayedText = "";

    /// <summary>Call on real typing so Enter can tell typed text from display text.</summary>
    public void MarkAddressEdited() => _boxEditedSinceDisplay = true;

    /// <summary>True when the box holds user typed input over the display text.</summary>
    public bool IsAddressEditedSinceDisplay => _boxEditedSinceDisplay;

    /// <summary>Records a programmatic box update as the unedited baseline.</summary>
    public void NoteAddressDisplayed(string text)
    {
        _lastDisplayedText = text?.Trim() ?? "";
        _boxEditedSinceDisplay = false;
    }

    /// <summary>Records a navigation and shows its domain in the box.</summary>
    public void ShowNavigatedUrl(string url)
    {
        CurrentPageUrl = url;
        SuppressSuggestionRefresh = true;
        AddressText = NavigationService.ToDisplayDomain(url);
        NoteAddressDisplayed(AddressText);
    }

    /// <summary>Reveals the full URL in the box for editing or copying.</summary>
    public void RevealFullUrl()
    {
        if (string.IsNullOrEmpty(CurrentPageUrl) || InternalUrls.IsInternal(CurrentPageUrl)) return;
        SuppressSuggestionRefresh = true;
        AddressText = NavigationService.FormatDisplayUrl(CurrentPageUrl);
        NoteAddressDisplayed(AddressText);
    }

    /// <summary>
    /// Resolves box input for Enter: untouched display text reloads the page,
    /// anything the user typed resolves like fresh input. Compares against a
    /// snapshot because binding keeps AddressText identical to the box.
    /// </summary>
    public string ResolveBarInput(string? boxText)
    {
        var typed = boxText?.Trim() ?? "";
        if (NavigationService.ShouldReload(typed, _lastDisplayedText, _boxEditedSinceDisplay, CurrentPageUrl))
            return CurrentPageUrl;
        return ResolveInput(typed);
    }

    public async Task UpdateSuggestionsAsync(string query)
    {
        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();
        var token = _suggestionCts.Token;
        _typedQuery = query;
        IsSuggestionTextDisplayed = false;
        SelectedSuggestionIndex = -1;

        if (string.IsNullOrWhiteSpace(query))
        {
            Suggestions.Clear();
            ShowSuggestions = false;
            return;
        }

        try
        {
            // Phase 1: local history and shortcuts render instantly so the
            // dropdown opens without waiting on the network.
            Suggestions.Clear();
            foreach (var r in _suggestionProvider.GetLocalSuggestions(query))
                Suggestions.Add(r);

            SelectedSuggestionIndex = Suggestions.Count > 0 ? 0 : -1;
            ShowSuggestions = Suggestions.Count > 0;

            // Icons resolve in the background from the favicon cache; rows
            // show their type icon until the site icon lands.
            _ = LoadRowFaviconsAsync(token);

            // Phase 2: web phrases append when they arrive. No clearing here,
            // so the list never flickers or rebuilds under the cursor.
            foreach (var item in await _suggestionProvider.GetWebSuggestionsAsync(query, token))
            {
                if (token.IsCancellationRequested) return;
                // A late response must not repopulate the list under a query the
                // user has already moved past, or the ghost reappears on its own.
                if (query != _typedQuery) return;
                if (Suggestions.Count >= SuggestionProvider.MaxSuggestions) break;
                if (!Suggestions.Any(r => string.Equals(r.DisplayText, item.DisplayText, StringComparison.OrdinalIgnoreCase)))
                {
                    Suggestions.Add(item);
                    // Select the first web result when there were no local matches.
                    // Later results must not replace a selection made with the arrows.
                    if (Suggestions.Count == 1) SelectedSuggestionIndex = 0;
                }
            }

            if (!token.IsCancellationRequested)
                ShowSuggestions = Suggestions.Count > 0;
        }
        catch (OperationCanceledException) { /* Ignoring cancellation */ }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"UpdateSuggestionsAsync error: {ex}"); }
    }

    /// <summary>
    /// Shuts the dropdown and drops any suggestion fetch still running. Enter has
    /// to call this rather than setting the flag alone: a fetch started when the
    /// user typed can land after the navigation and reopen the list.
    /// </summary>
    public void CloseSuggestions()
    {
        _suggestionCts?.Cancel();
        _suggestionCts = null;
        ShowSuggestions = false;
        SelectedSuggestionIndex = -1;
        IsSuggestionTextDisplayed = false;
    }

    /// <summary>
    /// Resolves site icons for rows that have a URL, newest rows first.
    /// These rows read the memory and disk caches only. A third-party icon request
    /// would publish every host in the user's history to the icon service on every
    /// keystroke, and typing a query is not consent to that.
    /// Skips rows that already have one and stops when superseded.
    /// </summary>
    private async Task LoadRowFaviconsAsync(CancellationToken token)
    {
        try
        {
            foreach (var row in Suggestions.ToList())
            {
                if (token.IsCancellationRequested) return;
                if (row.Favicon is not null || string.IsNullOrEmpty(row.Url)) continue;
                var icon = await _faviconLoader.LoadAsync(row.Url, FaviconLoadScope.LocalListRow);
                if (token.IsCancellationRequested) return;
                if (icon is not null && Suggestions.Contains(row))
                    row.Favicon = icon;
            }
        }
        catch (OperationCanceledException) { /* Ignoring cancellation */ }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"LoadRowFaviconsAsync error: {ex}"); }
    }
    /// <summary>
    /// Moves the suggestion highlight and fills the box with the highlighted
    /// row, like standard browser autocomplete. Returns false when there is
    /// nothing to move to. Up from the top row restores the typed text.
    /// </summary>
    public bool MoveSuggestionHighlight(int delta)
    {
        if (!ShowSuggestions || Suggestions.Count == 0) return false;

        var next = SelectedSuggestionIndex + delta;
        if (next < -1 || next >= Suggestions.Count) return false;

        if (SelectedSuggestionIndex == -1 && delta != 0)
            _typedQuery = AddressText;

        SelectedSuggestionIndex = next;
        IsSuggestionTextDisplayed = next >= 0;
        SuppressSuggestionRefresh = true;
        AddressText = next == -1 ? _typedQuery : Suggestions[next].DisplayText;
        return true;
    }

    /// <summary>Restores the typed text and clears the highlight.</summary>
    public void RestoreTypedQuery()
    {
        SelectedSuggestionIndex = -1;
        IsSuggestionTextDisplayed = false;
        SuppressSuggestionRefresh = true;
        AddressText = _typedQuery;
        NoteAddressDisplayed(AddressText);
    }

    /// <summary>Navigation target of the highlighted row, or null.</summary>
    public string? TakeHighlightedInput() =>
        SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count
            ? Suggestions[SelectedSuggestionIndex].NavigateTarget
            : null;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _engine.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _engine.GoForward();

    [RelayCommand]
    private void Refresh()
    {
        if (IsLoading) _engine.StopLoading();
        else _engine.Reload();
    }

    [RelayCommand]
    private async Task ToggleReader() => await _readerViewModel.ToggleAsync();

    [RelayCommand]
    private void HandleUpdateClick()
    {
        if (_isInstalling) return;
        if (UpdateState == UpdateState.UpdateAvailable)
        {
            _isInstalling = true;
            UpdateState = UpdateState.Downloading;
            UpdateProgress = 0.0;
            
            _ = Task.Run(async () => await _updateService.DownloadUpdateAsync());
        }
        else if (UpdateState == UpdateState.ReadyToInstall)
        {
            if (_isInstalling) return;
            _isInstalling = true;
            try
            {
                string flagFile = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "post_update.flag");
                System.IO.File.WriteAllText(flagFile, "true");
            }
            catch { }
            _updateService.InstallUpdate();
        }
    }
}

