using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StrideBrowser.Engine;
using StrideBrowser.Models;
using StrideBrowser.Services.CommandLine;

namespace StrideBrowser.Services.Startup;

public class StartupCoordinator
{
    private readonly TabEngine _engine;
    private readonly ICommandLineUrlParser _parser;
    private readonly BrowserSettings _settings;
    private readonly ISettingsStore _settingsStore;

    public StartupCoordinator(TabEngine engine, ICommandLineUrlParser parser, BrowserSettings settings, ISettingsStore settingsStore)
    {
        _engine = engine;
        _parser = parser;
        _settings = settings;
        _settingsStore = settingsStore;
    }

    public async Task<bool> HandleCommandLineArgsAsync(string[] args)
    {
        bool handled = false;
        foreach (var arg in args)
        {
            if (_parser.TryParse(arg, out var parsedUrl))
            {
                var tab = _engine.CreateTab(parsedUrl);
                _engine.SwitchTo(tab);
                await _engine.ActivateAsync(tab);
                handled = true;
                break;
            }
        }
        return handled;
    }

    public async Task<bool> HandleCommandLineAndReleaseNotesAsync(string[] args)
    {
        var handled = await HandleCommandLineArgsAsync(args);
        await HandleReleaseNotesAsync(openTab: !handled);
        return handled;
    }

    public async Task HandleReleaseNotesAsync(bool openTab = true)
    {
        string flagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "post_update.flag");
        bool isPostUpdate = false;
        
        if (File.Exists(flagFile))
        {
            try { File.Delete(flagFile); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            isPostUpdate = true;
        }
        
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.0";
        bool isNewVersion = false;
        if (_settings.LastSeenReleaseNotesVersion != currentVersion)
        {
            isNewVersion = true;
            _settings.LastSeenReleaseNotesVersion = currentVersion;
            _settingsStore.Save(_settings);
        }

        if (!_settings.HasCompletedOnboarding)
        {
            return;
        }

        if ((isPostUpdate || isNewVersion) && openTab)
        {
            var tab = _engine.CreateTab(InternalUrls.ReleaseNotes);
            _engine.SwitchTo(tab);
            await _engine.ActivateAsync(tab);
            _engine.NavigateToReleaseNotes(tab, currentVersion);
        }
    }
}


