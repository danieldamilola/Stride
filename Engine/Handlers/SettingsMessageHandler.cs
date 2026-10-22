using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services;

namespace StrideBrowser.Engine.Handlers;

public class SettingsMessageHandler : IWebMessageHandler, ISettingEmitter, IDisposable
{
    private readonly TabEngine _engine;
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateService _updateService;
    private readonly EventHandler<string> _onUpdateFailed;
    private readonly EventHandler<double> _onDownloadProgress;
    private bool _disposed;

    public event Action<string, string>? SettingChanged;

    /// <summary>Initializes a new instance of the SettingsMessageHandler.</summary>
    public SettingsMessageHandler(TabEngine engine, BrowserSettings settings, ISettingsStore settingsStore, UpdateService updateService)
    {
        _engine = engine;
        _settings = settings;
        _settingsStore = settingsStore;
        _updateService = updateService;
        // UpdateService is a singleton and this handler is scoped to one window, so the
        // callbacks have to be released when that window's scope ends or every window
        // ever opened stays reachable through the updater.
        _onUpdateFailed = (_, message) => _engine.PostMessageToSettingsTabs("update-failed:" + message);
        _onDownloadProgress = (_, progress) => _engine.PostMessageToSettingsTabs("update-progress:" + progress.ToString("F0", CultureInfo.InvariantCulture));
        _updateService.UpdateFailed += _onUpdateFailed;
        _updateService.DownloadProgressChanged += _onDownloadProgress;
    }

    /// <summary>Detaches the per-window subscriptions from the shared update service.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateService.UpdateFailed -= _onUpdateFailed;
        _updateService.DownloadProgressChanged -= _onDownloadProgress;
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns the message routes handled by this handler.</summary>
    public IEnumerable<MessageRoute> GetRoutes()
    {
        yield return MessageRoute.Prefix(WebMessagePrefix.Setting, HandleSetting);
        yield return MessageRoute.Prefix("shortcut-recording:", value =>
        {
            SettingChanged?.Invoke("shortcutRecording", value);
            return Task.CompletedTask;
        });
        yield return MessageRoute.Exact(WebMessagePrefix.ResetSettings, HandleResetSettings);
        yield return MessageRoute.Prefix("install-update:", async (_) => 
        { 
            bool downloaded = await _updateService.DownloadUpdateAsync();
            if (downloaded)
            {
                _updateService.InstallUpdate();
            }
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenBackgroundsFolder, HandleOpenBackgroundsFolder);
        yield return MessageRoute.Exact("check-for-update", async () =>
        {
            var status = await _updateService.CheckForUpdatesQuietlyAsync();
            string statusStr = status.HasValue ? status.Value.ToString().ToLowerInvariant() : "error";
            _engine.PostMessageToSettingsTabs($"update-check-result:{statusStr}:{_updateService.LatestVersion ?? ""}:");
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenReleaseNotes, async () =>
        {
            var tab = _engine.CreateTab(InternalUrls.ReleaseNotes);
            await _engine.ActivateAsync(tab);
        });
        yield return MessageRoute.Exact(WebMessagePrefix.OpenOnboarding, async () =>
        {
            var tab = _engine.CreateTab(InternalUrls.Onboarding);
            await _engine.ActivateAsync(tab);
        });
    }

    /// <summary>Resets all settings to defaults and reloads the settings page.</summary>
    private async Task HandleResetSettings()
    {
        var candidate = System.Text.Json.JsonSerializer.Deserialize<BrowserSettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;
        var keepOnboarding = candidate.HasCompletedOnboarding;
        candidate.ResetToDefaults();
        candidate.HasCompletedOnboarding = keepOnboarding;
        if (!_settingsStore.TrySave(candidate))
        {
            SendResult("reset", "", false, "Could not save the reset preferences.");
            return;
        }
        _settings.ResetToDefaults();
        _settings.HasCompletedOnboarding = keepOnboarding;
        SettingChanged?.Invoke("reset", "true");

        // A reset puts ad blocking back to its default. The profile has to follow now,
        // because the next page load is the first one that would notice otherwise.
        await _engine.ApplyAdBlockPreferenceAsync(_settings.AdBlockEnabled);

        if (_engine.ActiveTab?.Url == InternalUrls.Settings)
            _engine.NavigateToSettings(_engine.ActiveTab, _settings);
    }

    /// <summary>Opens the user backgrounds folder in the file explorer.</summary>
    private Task HandleOpenBackgroundsFolder()
    {
        var userBgFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stride", "Backgrounds");
        if (!System.IO.Directory.Exists(userBgFolder))
            System.IO.Directory.CreateDirectory(userBgFolder);

        Process.Start(new ProcessStartInfo { FileName = userBgFolder, UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void SendResult(string key, string value, bool success, string message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { key, value, success, message, restartRequired = SettingsChangePolicy.RequiresRestart(key) });
        _engine.PostMessageToSettingsTabs("setting-result:" + json);
        if (_engine.ActiveTab?.Url == InternalUrls.Onboarding)
            _engine.PostMessageToActiveTab("setting-result:" + json);
    }

    /// <summary>Handles a setting change message from the settings page.</summary>
    private async Task HandleSetting(string payload)
    {
        var parts = payload.Split(':', 2);
        if (parts.Length < 2) return;

        var key = parts[0];
        var value = parts[1];

        var candidate = System.Text.Json.JsonSerializer.Deserialize<BrowserSettings>(System.Text.Json.JsonSerializer.Serialize(_settings))!;
        if (!SettingsChangePolicy.TryApply(candidate, key, value, out var error))
        {
            SendResult(key, value, false, error);
            return;
        }
        if (!_settingsStore.TrySave(candidate))
        {
            SendResult(key, value, false, "Could not save settings. Check that the profile folder is writable.");
            return;
        }
        SettingsChangePolicy.TryApply(_settings, key, value, out _);
        SettingChanged?.Invoke(key, key == "shortcut" ? value.Split(':', 2)[0] : value);
        SendResult(key, value, true, SettingsChangePolicy.RequiresRestart(key) ? "Saved. Restart Stride to apply this change." : "Saved.");

        if (key == "zoom") _engine.ApplyDefaultZoom();

        // uBlock is a native extension, so the preference has to reach the profile
        // now rather than at the next page load. The coordinator enables or disables
        // the registered extension in place, with no reload and no remove and add
        // again, and keeps this newest value if a registration is still running.
        if (key == "adBlock")
            await _engine.ApplyAdBlockPreferenceAsync(_settings.AdBlockEnabled);

        // Live-reload: re-inject unhook script into YouTube tabs when settings change
        if (key.StartsWith("unhook", StringComparison.Ordinal))
            await _engine.ReInjectUnhookAsync();

        // Live-reload: apply enhancer settings to open YouTube tabs too
        if (key.StartsWith("yt", StringComparison.Ordinal))
            await _engine.ReInjectEnhancerAsync();
    }

}
