using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.ViewModels.Reader;

namespace StrideBrowser.Views;

/// <summary>
/// Scaffold code-behind. Keeps logic minimal per AGENTS.md. Real wiring in step 2 will
/// create the reader WebView2 lazily, set IsScriptEnabled = false, and hook NavigationStarting
/// plus NewWindowRequested to redirect to the page WebView2 and exit reader via IReaderService.
/// </summary>
public partial class ReaderView : UserControl
{
    public ReaderView()
    {
        InitializeComponent();
    }

    // Lazy per-tab reader WebView2 map is owned by the host window or a ReaderHostService.
    // Scaffold leaves this empty so the project compiles before WebView2 plumbing is filled in.
}
