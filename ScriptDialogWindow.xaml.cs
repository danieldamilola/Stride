using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser;

public partial class ScriptDialogWindow : Window, INotifyPropertyChanged
{
    private string _title = "Stride";
    public string TitleText
    {
        get => _title;
        set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleText))); }
    }

    private string _message = "";
    public string Message
    {
        get => _message;
        set { _message = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message))); }
    }

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { _inputText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText))); }
    }

    private Visibility _inputVisibility = Visibility.Collapsed;
    public Visibility InputVisibility
    {
        get => _inputVisibility;
        set { _inputVisibility = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputVisibility))); }
    }

    private Visibility _cancelVisibility = Visibility.Visible;
    public Visibility CancelVisibility
    {
        get => _cancelVisibility;
        set { _cancelVisibility = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CancelVisibility))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAccepted { get; private set; }

    public ScriptDialogWindow(CoreWebView2ScriptDialogKind kind, string message, string defaultText, string uri)
    {
        InitializeComponent();
        DataContext = this;

        var host = "";
        try { host = new Uri(uri).Host; } catch { }

        TitleText = string.IsNullOrEmpty(host) ? "This page says" : $"{host} says";
        Message = message;

        switch (kind)
        {
            case CoreWebView2ScriptDialogKind.Alert:
                CancelVisibility = Visibility.Collapsed;
                InputVisibility = Visibility.Collapsed;
                break;
            case CoreWebView2ScriptDialogKind.Confirm:
                CancelVisibility = Visibility.Visible;
                InputVisibility = Visibility.Collapsed;
                break;
            case CoreWebView2ScriptDialogKind.Prompt:
                CancelVisibility = Visibility.Visible;
                InputVisibility = Visibility.Visible;
                InputText = defaultText;
                break;
            case CoreWebView2ScriptDialogKind.Beforeunload:
                TitleText = "Leave site?";
                CancelVisibility = Visibility.Visible;
                InputVisibility = Visibility.Collapsed;
                BtnOk.Content = "Leave";
                BtnCancel.Content = "Stay";
                break;
        }
        
        if (InputVisibility == Visibility.Visible)
        {
            Loaded += (s, e) => { InputBox.Focus(); InputBox.SelectAll(); };
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        IsAccepted = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        IsAccepted = false;
        Close();
    }
}
