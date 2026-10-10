using System;
using System.Windows.Controls;
using System.Windows.Input;
using StrideBrowser.Engine;

namespace StrideBrowser.Services.UI;

public sealed class ToolbarNavigationController : IDisposable
{
    private readonly Button _reload;
    private readonly TabEngine _engine;

    public ToolbarNavigationController(Button reload, TabEngine engine)
    {
        _reload = reload;
        _engine = engine;
        reload.MouseRightButtonUp += OnReloadRightClick;
    }

    private void OnReloadRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var reload = new MenuItem { Header = "Reload", InputGestureText = "Ctrl+R" };
        reload.Click += (_, _) => _engine.Reload();
        var hardReload = new MenuItem { Header = "Reload without cache", InputGestureText = "Ctrl+F5" };
        hardReload.Click += async (_, _) =>
        {
            try { await _engine.HardReloadAsync(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Reload without cache failed: {ex}"); }
        };
        menu.Items.Add(reload);
        menu.Items.Add(hardReload);
        _reload.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    public void Dispose() => _reload.MouseRightButtonUp -= OnReloadRightClick;
}
