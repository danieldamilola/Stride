using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace StrideBrowser
{
    public partial class PopupWindow : Window
    {
        private readonly CoreWebView2Environment _environment;

        public PopupWindow(CoreWebView2Environment environment)
        {
            InitializeComponent();
            _environment = environment;
            
            // Wire up the close request so window.close() inside the popup closes the WPF Window
            PopupWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (e.IsSuccess && PopupWebView.CoreWebView2 != null)
                {
                    PopupWebView.CoreWebView2.WindowCloseRequested += (_, _) =>
                    {
                        Dispatcher.Invoke(Close);
                    };
                    
                    PopupWebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                    {
                        Title = PopupWebView.CoreWebView2.DocumentTitle;
                    };
                }
            };
        }

        public async Task InitializeAsync()
        {
            await PopupWebView.EnsureCoreWebView2Async(_environment);
        }
    }
}
