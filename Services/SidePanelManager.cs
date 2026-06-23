using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpurBrowser.Helpers;
using SpurBrowser.Interop;
using SpurBrowser.Models;

namespace SpurBrowser.Services;

public sealed class SidePanelManager
{
    private readonly Popup _popup;
    private readonly Window _owner;
    private readonly FrameworkElement _contentArea;
    private readonly FrameworkElement _contentPanel;
    private readonly Border _dockedSidebar;
    private readonly ColumnDefinition _dockedColumn;
    private readonly BrowserSettings _settings;
    private readonly DispatcherTimer _openDelay = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _closeDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _flashTimer = new();
    private readonly DispatcherTimer _edgeWatcher = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public const double Collapsed = 10;
    public const double Expanded = 240;
    private const double ContentTopOffset = 46;
    /// <summary>Pixels from the window edge where hover detection activates.</summary>
    private const double EdgeThreshold = 10;

    public bool IsPinned { get; private set; }
    public bool IsRightSide { get; set; } = true;

    private bool _suppressAutoHide;
    private bool _isMouseOver;
    private bool _isFlashing;
    private bool _popupZOrderFixed;
    private bool _isDeactivated;

    public SidePanelManager(Popup popup, Window owner, FrameworkElement contentArea,
        FrameworkElement contentPanel, Border dockedSidebar,
        ColumnDefinition dockedColumn, BrowserSettings settings)
    {
        _popup = popup;
        _owner = owner;
        _contentArea = contentArea;
        _contentPanel = contentPanel;
        _dockedSidebar = dockedSidebar;
        _dockedColumn = dockedColumn;
        _settings = settings;

        _popup.PlacementTarget = _owner;
        _popup.Placement = PlacementMode.Custom;
        _popup.CustomPopupPlacementCallback = PositionPopup;
        _popup.Width = Collapsed;
        _popup.PopupAnimation = PopupAnimation.None;

        _contentArea.SizeChanged += (_, _) => SyncContentHeight();
        _owner.SizeChanged += (_, _) => NudgePopup();
        _owner.LocationChanged += (_, _) => NudgePopup();

        _popup.Opened += OnPopupOpened;

        _owner.Deactivated += (_, _) =>
        {
            _isDeactivated = true;
            if (!IsPinned && _popup.IsOpen)
            {
                _openDelay.Stop();
                _closeDelay.Stop();
                _popup.IsOpen = false;
            }
        };
        _owner.Activated += (_, _) =>
        {
            _isDeactivated = false;
            if (!IsPinned)
            {
                _popup.IsOpen = true;
                _popup.Width = Collapsed;
                _contentPanel.Visibility = Visibility.Collapsed;
                SyncContentHeight();
                NudgePopup();
            }
        };

        _edgeWatcher.Tick += OnEdgeWatcherTick;

        if (_popup.Child is FrameworkElement child)
        {
            child.MouseEnter += OnContentMouseEnter;
            child.MouseLeave += OnContentMouseLeave;
        }

        _openDelay.Tick += (_, _) =>
        {
            _openDelay.Stop();
            Animate(Expanded);
        };
        _closeDelay.Tick += (_, _) =>
        {
            _closeDelay.Stop();
            if (!_suppressAutoHide && !IsPinned)
                Animate(Collapsed);
        };

        SyncContentHeight();
    }

    private void OnEdgeWatcherTick(object? sender, EventArgs e)
    {
        if (_isDeactivated || IsPinned || !_popup.IsOpen) return;

        var pos = Mouse.GetPosition(_owner);
        bool insideWindow = pos.X >= 0 && pos.X <= _owner.ActualWidth
                         && pos.Y >= 0 && pos.Y <= _owner.ActualHeight;
        bool nearEdge = IsRightSide
            ? pos.X >= _owner.ActualWidth - EdgeThreshold
            : pos.X <= EdgeThreshold;
        bool isAtEdge = insideWindow && nearEdge;

        if (isAtEdge && !_isMouseOver)
        {
            _isMouseOver = true;
            _suppressAutoHide = true;
            _closeDelay.Stop();
            _openDelay.Start();
        }
        else if (!isAtEdge && _isMouseOver)
        {
            _isMouseOver = false;
            _suppressAutoHide = false;
            if (!IsPinned)
            {
                _openDelay.Stop();
                _closeDelay.Start();
            }
        }
    }

    private void OnContentMouseEnter(object? sender, MouseEventArgs e)
    {
        _isMouseOver = true;
        _suppressAutoHide = true;
        _closeDelay.Stop();
        _openDelay.Start();
    }

    private void OnContentMouseLeave(object? sender, MouseEventArgs e)
    {
        _isMouseOver = false;
        _suppressAutoHide = false;
        if (!IsPinned)
        {
            _openDelay.Stop();
            _closeDelay.Start();
        }
    }

    private CustomPopupPlacement[] PositionPopup(Size popupSize, Size targetSize, Point offset)
    {
        double x = IsRightSide ? Math.Max(0, targetSize.Width - popupSize.Width) : 0;
        return [new CustomPopupPlacement(new Point(x, ContentTopOffset), PopupPrimaryAxis.None)];
    }

    private void NudgePopup()
    {
        var o = _popup.HorizontalOffset;
        _popup.HorizontalOffset = o + 1;
        _popup.HorizontalOffset = o;
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        _edgeWatcher.Start();

        if (_popupZOrderFixed) return;
        _popupZOrderFixed = true;

        if (_popup.Child is null) return;
        var source = PresentationSource.FromVisual(_popup.Child) as HwndSource;
        if (source is null) return;

        var popupHwnd = source.Handle;
        var ownerHwnd = new WindowInteropHelper(_owner).Handle;

        NativeMethods.SetWindowLong(popupHwnd, NativeMethods.GWL_HWNDPARENT, (int)ownerHwnd);
        int exStyle = NativeMethods.GetWindowLong(popupHwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(popupHwnd, NativeMethods.GWL_EXSTYLE,
            exStyle & ~NativeMethods.WS_EX_TOPMOST);
    }

    private void SyncContentHeight()
    {
        if (_contentArea.ActualHeight <= 0) return;
        if (_popup.Child is FrameworkElement child)
            child.Height = _contentArea.ActualHeight;
    }

    public void Animate(double to)
    {
        if (Math.Abs(_popup.Width - to) < 0.5) return;
        _contentPanel.Visibility = to > 30 ? Visibility.Visible : Visibility.Collapsed;
        var anim = new DoubleAnimation(_popup.Width, to, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new BackEase { Amplitude = 0.15, EasingMode = EasingMode.EaseOut }
        };
        _popup.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    public void TogglePin()
    {
        IsPinned = !IsPinned;
        if (IsPinned) Pin();
        else Unpin();
    }

    private void Pin()
    {
        _edgeWatcher.Stop();
        Animate(Expanded);
        _popup.IsOpen = false;
        _dockedSidebar.Visibility = Visibility.Visible;
        var anim = new GridLengthAnimation
        {
            From = new GridLength(Collapsed),
            To = new GridLength(Expanded),
            Duration = TimeSpan.FromMilliseconds(220)
        };
        _dockedColumn.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        _settings.IsSidebarPinned = true;
    }

    private void Unpin()
    {
        _dockedSidebar.Visibility = Visibility.Collapsed;
        var anim = new GridLengthAnimation
        {
            From = new GridLength(Expanded),
            To = new GridLength(Collapsed),
            Duration = TimeSpan.FromMilliseconds(220)
        };
        _dockedColumn.BeginAnimation(ColumnDefinition.WidthProperty, anim);

        if (!_isDeactivated)
        {
            _popup.IsOpen = true;
            _popup.Width = Collapsed;
            SyncContentHeight();
            NudgePopup();
        }
        _settings.IsSidebarPinned = false;
    }

    public void RestorePinnedState()
    {
        if (_settings.IsSidebarPinned)
        {
            IsPinned = true;
            _popup.IsOpen = false;
            _dockedSidebar.Visibility = Visibility.Visible;
            _dockedColumn.Width = new GridLength(Expanded);
        }
        else
        {
            _popup.Width = Collapsed;
            _dockedSidebar.Visibility = Visibility.Collapsed;
            _dockedColumn.Width = new GridLength(Collapsed);
            SyncContentHeight();
            NudgePopup();
            _popup.IsOpen = true;
        }
    }

    public void SetSuppressAutoHide(bool value)
    {
        _suppressAutoHide = value;
        if (!value && !IsPinned && !_isDeactivated && !_isMouseOver)
        {
            _closeDelay.Stop();
            Animate(Collapsed);
        }
    }

    public void SetFullscreen(bool entering)
    {
        if (entering)
        {
            _openDelay.Stop();
            _closeDelay.Stop();
            _popup.IsOpen = false;
            _edgeWatcher.Stop();
        }
        else if (!IsPinned && !_isDeactivated)
        {
            _popup.IsOpen = true;
            _popup.Width = Collapsed;
            SyncContentHeight();
            NudgePopup();
            _edgeWatcher.Start();
        }
    }

    public void FlashPeek(TimeSpan duration)
    {
        if (IsPinned || _isFlashing) return;
        _isFlashing = true;
        _openDelay.Stop();
        _closeDelay.Stop();
        Animate(Expanded);

        void OnTick(object? s, EventArgs e)
        {
            _flashTimer.Stop();
            _flashTimer.Tick -= OnTick;
            _isFlashing = false;
            if (!IsPinned && !_isDeactivated && !_suppressAutoHide)
                Animate(Collapsed);
        }
        _flashTimer.Tick += OnTick;
        _flashTimer.Interval = duration;
        _flashTimer.Start();
    }
}
