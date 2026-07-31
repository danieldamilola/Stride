using CommunityToolkit.Mvvm.ComponentModel;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.ViewModels;

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

    [ObservableProperty]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    private double _activeDownloadsProgress;

    public BrowserViewModel(BrowserSettings settings, NavigationService navigation, IDownloadStore downloadStore)
    {
        Settings = settings;
        _navigation = navigation;
        
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
}
