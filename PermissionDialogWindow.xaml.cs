using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser;

public partial class PermissionDialogWindow : Window, INotifyPropertyChanged
{
    private string _hostTitle = "";
    public string HostTitle
    {
        get => _hostTitle;
        set { _hostTitle = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostTitle))); }
    }

    private string _requestMessage = "";
    public string RequestMessage
    {
        get => _requestMessage;
        set { _requestMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequestMessage))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAllowed { get; private set; }

    public PermissionDialogWindow(string uri, CoreWebView2PermissionKind kind)
    {
        InitializeComponent();
        DataContext = this;

        var host = "";
        try { host = new Uri(uri).Host; } catch { }

        HostTitle = string.IsNullOrEmpty(host) ? "This site" : host;
        
        string permissionName = kind switch
        {
            CoreWebView2PermissionKind.Microphone => "your microphone",
            CoreWebView2PermissionKind.Camera => "your camera",
            CoreWebView2PermissionKind.Geolocation => "your location",
            CoreWebView2PermissionKind.Notifications => "show notifications",
            CoreWebView2PermissionKind.OtherSensors => "motion and light sensors",
            CoreWebView2PermissionKind.ClipboardRead => "read your clipboard",
            CoreWebView2PermissionKind.LocalFonts => "access local fonts",
            _ => "access a restricted feature"
        };

        RequestMessage = $"Wants to {permissionName}.";
    }

    private void BtnAllow_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = true;
        Close();
    }

    private void BtnDeny_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = false;
        Close();
    }
}
