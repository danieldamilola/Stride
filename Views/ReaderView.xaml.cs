using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.ViewModels.Reader;

namespace StrideBrowser.Views;

/// <summary>
/// Reader overlay host. Holds the dedicated reader WebView2 which is created lazily with script disabled.
/// </summary>
public partial class ReaderView : UserControl
{
    public ReaderView()
    {
        InitializeComponent();
    }

    public void SetWebView(FrameworkElement webView)
    {
        ReaderHost.Children.Clear();
        ReaderHost.Children.Add(webView);
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = System.Windows.Visibility.Visible;
    }

    public void HideError()
    {
        ErrorText.Visibility = System.Windows.Visibility.Collapsed;
    }
}
