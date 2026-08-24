using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StrideBrowser.Models;
using StrideBrowser.Services;

using StrideBrowser.Services.Reader;
using StrideBrowser.ViewModels.Reader;

namespace StrideBrowser.ViewModels;

/// <summary>
/// Thin coordinator between the UI and the engine/services.
/// Contains NO business logic - only UI state and command routing.
/// </summary>
public sealed partial class BrowserViewModel : ObservableObject
{
    private static readonly HttpClient _http = new();
    private CancellationTokenSource? _suggestionCts;
    private readonly NavigationService _navigation;

    public BrowserSettings Settings { get; }

    [ObservableProperty]
    private string _addressText = "";

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<string> _suggestions = new();

    [ObservableProperty]
    private int _selectedSuggestionIndex = -1;

    [ObservableProperty]
    private bool _showSuggestions;

    /// <summary>True while the active tab is navigating - drives the address bar loading cues.</summary>
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    private double _activeDownloadsProgress;

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

    private readonly Engine.TabEngine _engine;
    private readonly IReaderService _readerService;
    private readonly ReaderViewModel _readerViewModel;
    private readonly UpdateService _updateService;
    private bool _isInstalling;

    public BrowserViewModel(BrowserSettings settings, NavigationService navigation, IDownloadStore downloadStore, Engine.TabEngine engine, IReaderService readerService, ReaderViewModel readerViewModel, UpdateService updateService)
    {
        Settings = settings;
        _navigation = navigation;
        _engine = engine;
        _readerService = readerService;
        _readerViewModel = readerViewModel;
        _updateService = updateService;

        _updateService.UpdateAvailable += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                UpdateState = UpdateState.UpdateAvailable;
                UpdateVersion = _updateService.LatestVersion ?? "";
            });
        };

        _updateService.DownloadProgressChanged += (s, progress) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                UpdateState = UpdateState.Downloading;
                UpdateProgress = progress / 100.0;
            });
        };

        _updateService.DownloadCompleted += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _isInstalling = false;
                UpdateState = UpdateState.ReadyToInstall;
            });
        };

        _updateService.UpdateFailed += (s, e) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _isInstalling = false;
                UpdateState = UpdateState.UpdateAvailable;
                UpdateProgress = 0;
            });
        };

        downloadStore.Items.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (DownloadItem item in e.NewItems)
                    item.PropertyChanged += OnDownloadItemChanged;
            }
            if (e.OldItems != null)
            {
                foreach (DownloadItem item in e.OldItems)
                    item.PropertyChanged -= OnDownloadItemChanged;
            }
            UpdateDownloadProgress(downloadStore.Items);
        };

        foreach (var item in downloadStore.Items)
            item.PropertyChanged += OnDownloadItemChanged;
            
        UpdateDownloadProgress(downloadStore.Items);

        void OnDownloadItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadItem.ReceivedBytes) or nameof(DownloadItem.TotalBytes) or nameof(DownloadItem.State))
            {
                UpdateDownloadProgress(downloadStore.Items);
            }
        }
    }

    private void UpdateDownloadProgress(IEnumerable<DownloadItem> items)
    {
        var active = items.Where(i => i.State == DownloadState.InProgress).ToList();
        HasActiveDownloads = active.Count > 0;

        if (!HasActiveDownloads)
        {
            ActiveDownloadsProgress = 0;
            return;
        }

        long totalBytes = 0;
        long receivedBytes = 0;
        bool allUnknown = true;

        foreach (var item in active)
        {
            if (item.TotalBytes > 0)
            {
                totalBytes += item.TotalBytes;
                receivedBytes += item.ReceivedBytes;
                allUnknown = false;
            }
            else
            {
                // If we don't know the size, just add some arbitrary amount so the progress bar isn't empty
                totalBytes += 100;
                receivedBytes += 10; 
            }
        }

        if (allUnknown)
        {
            // Indeterminate-like tiny sliver
            ActiveDownloadsProgress = 0.1;
        }
        else
        {
            ActiveDownloadsProgress = (double)receivedBytes / totalBytes;
        }
    }

    /// <summary>Resolves a raw input string into a URL.</summary>
    public string ResolveInput(string input) => _navigation.Resolve(input);

    /// <summary>Updates the address bar from the active tab's state.</summary>
    public void SyncAddressBar(BrowserTab tab)
    {
        AddressText = InternalUrls.IsInternal(tab.Url) ? "" : tab.Url;
    }

    public async Task UpdateSuggestionsAsync(string query)
    {
        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();
        var token = _suggestionCts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            Suggestions.Clear();
            ShowSuggestions = false;
            return;
        }

        try
        {
            var results = new List<string>();
            var lowerQuery = query.ToLowerInvariant();
            
            // 1. Instantly show local shortcuts
            foreach (var shortcut in Settings.NewTabShortcuts)
            {
                if (shortcut.Name.ToLowerInvariant().Contains(lowerQuery) || 
                    shortcut.Url.ToLowerInvariant().Contains(lowerQuery))
                {
                    results.Add(shortcut.Url);
                }
            }
            
            Suggestions.Clear();
            foreach (var r in results.Take(8))
                Suggestions.Add(r);
            
            SelectedSuggestionIndex = -1;
            ShowSuggestions = Suggestions.Count > 0;

            // 2. Debounce and fetch web suggestions
            await Task.Delay(150, token); 
            
            var url = $"https://duckduckgo.com/ac/?q={Uri.EscapeDataString(query)}";
            var response = await _http.GetStringAsync(url, token);
            
            if (token.IsCancellationRequested) return;

            // [{ "phrase": "..." }, ...]
            using var doc = JsonDocument.Parse(response);
            
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("phrase", out var phrase))
                {
                    var p = phrase.GetString();
                    if (!string.IsNullOrEmpty(p) && !results.Contains(p))
                        results.Add(p);
                }
            }

            if (!token.IsCancellationRequested)
            {
                Suggestions.Clear();
                foreach (var r in results.Take(8))
                    Suggestions.Add(r);

                SelectedSuggestionIndex = -1;
                ShowSuggestions = Suggestions.Count > 0;
            }
        }
        catch (OperationCanceledException) { /* Ignoring cancellation */ }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"UpdateSuggestionsAsync error: {ex}"); }
    }

    [RelayCommand]
    private void GoBack() => _engine.GoBack();

    [RelayCommand]
    private void GoForward() => _engine.GoForward();

    [RelayCommand]
    private void Refresh() => _engine.Reload();

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

