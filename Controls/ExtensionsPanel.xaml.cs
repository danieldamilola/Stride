using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace StrideBrowser.Controls;

public partial class ExtensionsPanel : UserControl
{
    public ExtensionsPanel() => InitializeComponent();
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }
}
