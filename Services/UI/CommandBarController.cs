using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.ViewModels;

namespace StrideBrowser.Services.UI;

public sealed class CommandBarController
{
    private readonly BrowserViewModel _vm;
    private readonly TabEngine _engine;
    
    // UI Elements
    private readonly Grid _commandBarGrid;
    private readonly Border _commandBarPanel;
    private readonly TextBox _addressBar;
    private readonly TextBox _standardAddressBar;
    private readonly TextBlock _urlLabel;
    private readonly UIElement _webViewHost;
    private readonly Dispatcher _dispatcher;

    private bool _isCommandBarOpen;

    public CommandBarController(
        BrowserViewModel vm,
        TabEngine engine,
        Grid commandBarGrid,
        Border commandBarPanel,
        TextBox addressBar,
        TextBox standardAddressBar,
        TextBlock urlLabel,
        UIElement webViewHost,
        Dispatcher dispatcher)
    {
        _vm = vm;
        _engine = engine;
        _commandBarGrid = commandBarGrid;
        _commandBarPanel = commandBarPanel;
        _addressBar = addressBar;
        _standardAddressBar = standardAddressBar;
        _urlLabel = urlLabel;
        _webViewHost = webViewHost;
        _dispatcher = dispatcher;
    }

    public void FocusAddressBar()
    {
        if (_vm.Settings.UseFloatingCommandBar)
        {
            ShowCommandBar();
        }
        else
        {
            _standardAddressBar.Focus();
            if (_engine.ActiveTab is { } activeTab && !string.IsNullOrEmpty(activeTab.Url) && !InternalUrls.IsInternal(activeTab.Url))
            {
                _standardAddressBar.Text = activeTab.Url;
            }
            else
            {
                _standardAddressBar.Text = "";
            }
            _standardAddressBar.SelectAll();
        }
    }

    public void ShowCommandBar()
    {
        if (_isCommandBarOpen) return;
        _isCommandBarOpen = true;

        if (_engine.ActiveTab is { } activeTab && !string.IsNullOrEmpty(activeTab.Url) && !InternalUrls.IsInternal(activeTab.Url))
            _vm.AddressText = activeTab.Url;
        else
            _vm.AddressText = "";

        _commandBarGrid.Visibility = Visibility.Visible;

        _commandBarGrid.Opacity = 0;
        _commandBarPanel.RenderTransform = new TranslateTransform(0, -10);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slideIn = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(80)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        _commandBarGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _commandBarPanel.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);

        _dispatcher.BeginInvoke(new Action(() =>
        {
            _addressBar.Focus();
            Keyboard.Focus(_addressBar);
            _addressBar.SelectAll();
        }), DispatcherPriority.Input);
    }

    public void HideCommandBar()
    {
        if (!_isCommandBarOpen) return;
        _isCommandBarOpen = false;

        _vm.ShowSuggestions = false;
        _vm.Suggestions.Clear();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fadeOut.Completed += (_, _) =>
        {
            _commandBarGrid.Visibility = Visibility.Collapsed;
            _commandBarGrid.BeginAnimation(UIElement.OpacityProperty, null);

            if (_engine.ActiveTab is not null)
                _webViewHost.Focus();
        };

        var slideOut = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(80)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        _commandBarGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        if (_commandBarPanel.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    public void HandleAddressTextChanged(TextBox? tb)
    {
        if (tb is not null && tb.IsKeyboardFocusWithin)
            _ = _vm.UpdateSuggestionsAsync(tb.Text);
    }

    public void NavigateToSuggestion(string input)
    {
        var url = _vm.ResolveInput(input);
        if (_engine.ActiveTab is { } tab)
        {
            tab.Url = url;
            _vm.AddressText = url;
            _engine.Navigate(tab, url);
        }
        _vm.ShowSuggestions = false;
    }

    public void UpdateUrlLabel(BrowserTab tab)
    {
        if (!_standardAddressBar.IsKeyboardFocusWithin)
            _standardAddressBar.Text = tab.Url;

        if (string.IsNullOrEmpty(tab.Url) || InternalUrls.IsInternal(tab.Url))
        {
            _urlLabel.Text = tab.Title ?? "New Tab";
            return;
        }

        if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
            var path = uri.PathAndQuery;
            if (path == "/") path = "";

            _urlLabel.Text = host + path;
        }
        else
        {
            _urlLabel.Text = tab.Url;
        }
    }
}
