using CommunityToolkit.Mvvm.ComponentModel;
using StrideBrowser.Models.Reader;
using StrideBrowser.Services.Reader;

namespace StrideBrowser.ViewModels.Reader;

/// <summary>
/// Single shared VM that mirrors the active tab session owned by IReaderService.
/// Service owns truth keyed by tabId. VM re-derives bindable state on ActiveTabChanged or SessionChanged.
/// No tabId on public commands — VM resolves ActiveTabId internally.
/// </summary>
public sealed partial class ReaderViewModel : ObservableObject
{
    private readonly IReaderService _readerService;
    private readonly Func<Guid?> _getActiveTabId;

    [ObservableProperty]
    private bool _isReaderAvailable;

    [ObservableProperty]
    private bool _isInReader;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ReaderContent? _current;

    [ObservableProperty]
    private string? _error;

    public ReaderViewModel(IReaderService readerService, Func<Guid?> getActiveTabId)
    {
        _readerService = readerService;
        _getActiveTabId = getActiveTabId;

        _readerService.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, Guid tabId)
    {
        var activeId = _getActiveTabId();
        if (activeId is null || activeId.Value != tabId)
        {
            // Background tab changed silently. Only resync if it is now the active tab.
            return;
        }

        SyncFromService(activeId.Value);
    }

    public void OnActiveTabChanged(Guid? tabId)
    {
        if (tabId is null)
        {
            IsReaderAvailable = false;
            IsInReader = false;
            Current = null;
            Error = null;
            return;
        }

        SyncFromService(tabId.Value);
        // Availability is updated on next CanEnterReaderAsync call. Keep current IsReaderAvailable until then.
    }

    private void SyncFromService(Guid tabId)
    {
        var session = _readerService.GetSession(tabId);
        IsInReader = session?.IsInReader ?? false;
        Current = session?.Current;
        Error = null;
    }

    public async Task<bool> RefreshAvailabilityAsync()
    {
        var tabId = _getActiveTabId();
        if (tabId is null)
        {
            IsReaderAvailable = false;
            return false;
        }

        try
        {
            IsReaderAvailable = await _readerService.CanEnterReaderAsync(tabId.Value);
            return IsReaderAvailable;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Reader availability check failed: {ex.Message}");
            IsReaderAvailable = false;
            return false;
        }
    }

    public async Task ToggleAsync()
    {
        if (IsInReader)
            await ExitAsync();
        else
            await EnterAsync();
    }

    public async Task EnterAsync()
    {
        var tabId = _getActiveTabId();
        if (tabId is null) return;

        IsLoading = true;
        Error = null;
        try
        {
            // Service continuation writes only into per-tab session keyed by tabId.
            // VM re-derives via SessionChanged, not via direct assignment from awaiting task.
            await _readerService.EnterReaderAsync(tabId.Value);
            SyncFromService(tabId.Value);
        }
        catch (NotImplementedException ex)
        {
            Error = "Reader not yet implemented in this scaffold: " + ex.Message;
            System.Diagnostics.Trace.WriteLine($"Reader enter failed scaffold: {ex}");
            System.Diagnostics.Debug.WriteLine($"Reader enter failed scaffold: {ex}");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            System.Diagnostics.Trace.WriteLine($"Reader enter failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"Reader enter failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExitAsync()
    {
        var tabId = _getActiveTabId();
        if (tabId is null) return;

        try
        {
            await _readerService.ExitReaderAsync(tabId.Value);
            SyncFromService(tabId.Value);
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Reader exit failed: {ex.Message}");
        }
    }

    public async Task RefreshAsync()
    {
        var tabId = _getActiveTabId();
        if (tabId is null) return;

        IsLoading = true;
        try
        {
            await _readerService.RefreshAsync(tabId.Value);
            SyncFromService(tabId.Value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Reader refresh failed: {ex.Message}");
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
