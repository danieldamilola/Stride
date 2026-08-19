using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Services;

namespace StrideBrowser.Services.UI;

/// <summary>
/// Handles launching the T&amp;C Lens extension options page and capturing
/// active page text for the native T&amp;C Lens bridge.
/// </summary>
public sealed class TCLensLauncher
{
    private readonly TabEngine _engine;
    private readonly TCLensTransferService _tcLensTransfer;

    public TCLensLauncher(TabEngine engine, TCLensTransferService tcLensTransfer)
    {
        _engine = engine;
        _tcLensTransfer = tcLensTransfer;
    }

    /// <summary>
    /// Launches or focuses the installed T&amp;C Lens extension options page.
    /// </summary>
    public async Task LaunchTCLensAsync()
    {
        var wv = _engine.GetCoreWebView2();
        if (wv == null) return;

        try
        {
            var exts = await wv.Profile.GetBrowserExtensionsAsync();
            var tcLens = exts.FirstOrDefault(e =>
                e.Name.Contains("T&C Lens", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("T-C", StringComparison.OrdinalIgnoreCase));

            if (tcLens != null)
            {
                var url = $"extension://{tcLens.Id}/options/options.html";

                // Look for existing tab
                var existing = _engine.Tabs.FirstOrDefault(t =>
                    t.Url != null && t.Url.StartsWith($"extension://{tcLens.Id}/options", StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    _engine.SwitchTo(existing);
                }
                else
                {
                    var tab = _engine.CreateTab(url);
                    _engine.SwitchTo(tab);
                    await _engine.ActivateAsync(tab);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to launch T&C Lens: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts innerText of the active tab, sets pending transfer data, and navigates to the TCLens options page.
    /// </summary>
    public async Task HandleNativeTCLensShortcutAsync()
    {
        try
        {
            var activeTab = _engine.ActiveTab;
            if (activeTab == null) return;

            var wv = _engine.GetCoreWebView2();
            if (wv == null) return;

            var rawJson = await wv.ExecuteScriptAsync("document.body.innerText");
            if (!string.IsNullOrEmpty(rawJson) && rawJson != "null")
            {
                _tcLensTransfer.PendingText = JsonSerializer.Deserialize<string>(rawJson) ?? "";
            }
            _tcLensTransfer.PendingUrl = activeTab.Url ?? "";
            _tcLensTransfer.PendingTitle = activeTab.Title ?? "";

            var newTab = _engine.CreateTab("http://local.assets/TCLens/options/options.html");
            _engine.SwitchTo(newTab);
            await _engine.ActivateAsync(newTab);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"HandleNativeTCLensShortcutAsync failed: {ex}");
        }
    }
}
