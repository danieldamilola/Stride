using System.ComponentModel;
using System.Windows;

namespace StrideBrowser;

public partial class BaseBrowserDialogWindow : Window, INotifyPropertyChanged
{
    private string _dialogTitle = "";
    public string DialogTitle
    {
        get => _dialogTitle;
        set { _dialogTitle = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DialogTitle))); }
    }

    private string _dialogMessage = "";
    public string DialogMessage
    {
        get => _dialogMessage;
        set { _dialogMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DialogMessage))); }
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

    private string _okButtonText = "OK";
    public string OkButtonText
    {
        get => _okButtonText;
        set { _okButtonText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OkButtonText))); }
    }

    private string _cancelButtonText = "Cancel";
    public string CancelButtonText
    {
        get => _cancelButtonText;
        set { _cancelButtonText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CancelButtonText))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAccepted { get; private set; }

    public BaseBrowserDialogWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        Loaded += (s, e) => 
        {
            if (InputVisibility == Visibility.Visible)
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
        };
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
