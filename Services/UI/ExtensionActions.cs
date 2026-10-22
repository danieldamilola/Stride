using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services.Extensions;

namespace StrideBrowser.Services.UI;

/// <summary>Window-owned extension actions shared by the toolbar and internal page.</summary>
public sealed class ExtensionActions(TabEngine engine, ExtensionLibrary library, BrowserSettings settings, ISettingsStore store) : IDisposable
{
    public event Action<string, string>? SettingChanged;
    public Window? Owner { get; set; }
    public Func<string, FrameworkElement?>? AnchorForExtension { get; set; }
    private ExtensionActionPopup? _actionPopup;
    private Window? _extensionWindow;
    private bool _disposed;

    public async Task<CoreWebView2> ReadyAsync()
    {
        var tab = engine.ActiveTab ?? throw new InvalidOperationException("Open a browser tab first.");
        var core = await engine.EnsureTabReadyAsync(tab, CancellationToken.None)
            ?? throw new InvalidOperationException("The browser is not ready.");
        var result = await engine.ApplyAdBlockPreferenceAsync(settings.AdBlockEnabled);
        library.RememberBuiltIn(core, result.Native);
        return core;
    }
    public async Task<ExtensionSnapshot> SnapshotAsync()
    {
        var native = await library.SnapshotAsync(await ReadyAsync());
        return native with { Extensions = native.Extensions.Concat(BuiltInExtensions.List(settings)).ToArray() };
    }
    public async Task LoadAsync()
    {
        if (!library.DeveloperMode) throw new InvalidOperationException("Turn on Developer mode to load an extension.");
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Select the extension folder containing manifest.json", Multiselect = false };
        if (picker.ShowDialog(Owner) != true) return;
        await library.LoadAsync(await ReadyAsync(), picker.FolderName);
    }
    public async Task ToggleAsync(string id, bool enabled)
    {
        if (BuiltInExtensions.Contains(id))
        {
            var key = BuiltInExtensions.SettingKey(id);
            var value = enabled ? "true" : "false";
            var updated = JsonSerializer.Deserialize<BrowserSettings>(JsonSerializer.Serialize(settings))!;
            if (!SettingsChangePolicy.TryApply(updated, key, value, out var error)) throw new InvalidOperationException(error);
            if (!store.TrySave(updated)) throw new InvalidOperationException("The preference could not be saved.");
            SettingsChangePolicy.TryApply(settings, key, value, out _);
            SettingChanged?.Invoke(key, value);
            if (key == "unhookEnabled") await engine.ReInjectUnhookAsync();
            if (key == "ytEnhancerEnabled") await engine.ReInjectEnhancerAsync();
            engine.PostMessageToSettingsTabs("setting-result:" + JsonSerializer.Serialize(new
            { key, value, success = true, message = "", restartRequired = SettingsChangePolicy.RequiresRestart(key) }));
            library.NotifyChanged();
            return;
        }
        var core = await ReadyAsync();
        if (!library.IsBuiltIn(core, id)) { await library.ToggleAsync(core, id, enabled); return; }
        var candidate = JsonSerializer.Deserialize<BrowserSettings>(JsonSerializer.Serialize(settings))!;
        candidate.AdBlockEnabled = enabled;
        if (!store.TrySave(candidate)) throw new InvalidOperationException("The ad blocking preference could not be saved.");
        settings.AdBlockEnabled = enabled;
        var result = await engine.ApplyAdBlockPreferenceAsync(enabled);
        library.RememberBuiltIn(core, result.Native);
        library.NotifyChanged();
        if (!result.Succeeded) throw new InvalidOperationException(result.Detail ?? "The browser could not change ad blocking.");
        engine.PostMessageToSettingsTabs("setting-result:" + JsonSerializer.Serialize(new
        { key = "adBlock", value = enabled.ToString().ToLowerInvariant(), success = true, message = "", restartRequired = false }));
    }
    public async Task ReloadAllAsync()
    {
        var core = await ReadyAsync();
        var snapshot = await library.SnapshotAsync(core);
        var failures = new List<string>();
        foreach (var item in snapshot.Extensions.Where(x => !x.IsBuiltIn))
            try { await library.ReloadAsync(core, item.Id); }
            catch (Exception ex) { failures.Add(item.Name + ": " + ex.Message); }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }
    public async Task OpenAsync(string id, bool options = false)
    {
        if (BuiltInExtensions.Contains(id))
        {
            var section = BuiltInExtensions.Section(id);
            var tab = engine.Tabs.FirstOrDefault(t => t.Url == InternalUrls.Settings) ?? engine.CreateTab(InternalUrls.Settings);
            engine.SwitchTo(tab); await engine.ActivateAsync(tab);
            var settingsCore = await engine.EnsureTabReadyAsync(tab);
            for (var attempt = 0; attempt < 80 && settingsCore is not null && tab.Url == InternalUrls.Settings; attempt++)
            {
                if (tab.ActiveInternalDocument?.PageKind == TabEngine.InternalPageKind.Settings &&
                    await settingsCore.ExecuteScriptAsync("(()=>{if(document.readyState!=='complete')return false;const b=document.querySelector('[data-target=\"" + section + "\"]');if(!b)return false;b.click();return b.classList.contains('active');})()") == "true") return;
                await Task.Delay(50);
            }
            return;
        }
        var core = await ReadyAsync();
        var uri = await library.GetPageUriAsync(core, id, options);
        var environment = engine.WebViewEnvironment ?? throw new InvalidOperationException("The browser is not ready.");
        if (!options)
        {
            _actionPopup?.Dispose();
            var owner = Owner ?? throw new InvalidOperationException("The browser window is not available.");
            var anchor = AnchorForExtension?.Invoke(id) ?? throw new InvalidOperationException("The extensions toolbar is not available.");
            _actionPopup = new ExtensionActionPopup(owner, engine, anchor);
            await _actionPopup.ShowAsync(environment, core.Profile.ProfileName, uri, library.DeveloperMode, OpenExternal,
                library.IsBuiltIn(core, id) ? core.Source : null, engine.ActiveTab?.Title);
            return;
        }
        _extensionWindow?.Close();
        var view = new WebView2();
        var window = new Window
        {
            Owner = Owner, Title = options ? "Extension options" : "Extension", Content = view,
            Width = options ? 760 : 420, Height = options ? 650 : 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize
        };
        _extensionWindow = window;
        window.Closed += (_, _) => { view.Dispose(); if (_extensionWindow == window) _extensionWindow = null; };
        window.Show();
        try
        {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = core.Profile.ProfileName;
            await view.EnsureCoreWebView2Async(environment, controllerOptions);
            if (_disposed || !window.IsVisible) return;
            view.CoreWebView2.Settings.IsWebMessageEnabled = false;
            view.CoreWebView2.Settings.AreDevToolsEnabled = library.DeveloperMode;
            view.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var next) && next.Scheme == uri.Scheme && next.Host == uri.Host) return;
                e.Cancel = true;
                OpenExternal(e.Uri);
            };
            view.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
            view.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        catch { window.Close(); throw; }
    }
    private void OpenExternal(string url) => _ = OpenExternalAsync(url);
    private async Task OpenExternalAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try { var tab = engine.CreateTab(url); engine.SwitchTo(tab); await engine.ActivateAsync(tab); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }
    public void Dispose() { _disposed = true; _actionPopup?.Dispose(); _extensionWindow?.Close(); Owner = null; AnchorForExtension = null; }
}
