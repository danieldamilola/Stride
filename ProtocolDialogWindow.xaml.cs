using System;
using System.ComponentModel;
using System.Windows;

namespace StrideBrowser;

public partial class ProtocolDialogWindow : Window, INotifyPropertyChanged
{
    private string _requestMessage = "";
    public string RequestMessage
    {
        get => _requestMessage;
        set { _requestMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequestMessage))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAllowed { get; private set; }

    public ProtocolDialogWindow(string url)
    {
        InitializeComponent();
        DataContext = this;

        // Truncate URL if too long
        var displayUrl = url.Length > 100 ? url.Substring(0, 97) + "..." : url;
        RequestMessage = $"This page wants to open an external application to handle this link:\n\n{displayUrl}\n\nOnly continue if you trust this site.";
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        IsAllowed = false;
        Close();
    }
}
