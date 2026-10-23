using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using UmberBrowser.Helpers;

namespace UmberBrowser.Services.Extensions;

public sealed record InstalledExtension(string Id, string Name, string Description, string Version,
    bool IsEnabled, bool IsBuiltIn, bool IsPinned, string IconDataUri, bool HasPopup, bool HasOptions,
    IReadOnlyList<string> Permissions, string SourceLabel, string Path, string? Error, bool CanToggle = true);
public sealed record ExtensionSnapshot(bool DeveloperMode, IReadOnlyList<InstalledExtension> Extensions, string? Error = null);

/// <summary>Native profile inventory plus the source folders chosen through umber.</summary>
public sealed class ExtensionLibrary
{
    public const int MaxPinnedExtensions = 3;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _file;
    private Preferences _preferences;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ExtensionNativeState> _builtIns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Profile, string Id), string> _profileSources = new();
    private readonly HashSet<string> _components = new(StringComparer.Ordinal)
    { "dgiklkfkllikcanfonkcabmbdfmgleag", "mhjfbmdgcfjbbpaeojofohoefgiehjai" };
    public event Action? Changed;
    public bool DeveloperMode => _preferences.DeveloperMode;
    public void NotifyChanged() => Changed?.Invoke();

    public ExtensionLibrary() : this(System.IO.Path.Combine(AppPaths.DataDirectory, "extension-library.json")) { }
    public ExtensionLibrary(string file)
    {
        _file = file;
        try
        {
            _preferences = File.Exists(file) && new FileInfo(file).Length <= 4 * 1024 * 1024
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file), JsonOptions) ?? new() : new();
            _preferences.Sources ??= new();
            _preferences.Pinned ??= new();
            _preferences.Names ??= new();
            _preferences.Enabled ??= new();
        }
        catch { _preferences = new(); }
    }

    public void RememberBuiltIn(CoreWebView2 core, ExtensionNativeState state)
    {
        if (state.Registered && state.ExtensionId is not null)
            _builtIns[core.Profile.ProfilePath] = state;
    }

    public async Task<ExtensionSnapshot> SnapshotAsync(CoreWebView2 core)
    {
        await _gate.WaitAsync();
        try
        {
            var profile = core.Profile.ProfilePath;
            var profileEntries = await Task.Run(() => ExtensionProfileReader.Read(profile));
            foreach (var entry in profileEntries)
            {
                if (entry.IsComponent) _components.Add(entry.Id);
                else _profileSources[(profile, entry.Id)] = entry.Folder;
            }
            var native = await core.Profile.GetBrowserExtensionsAsync();
            var descriptors = native.Where(ext => !_components.Contains(ext.Id)).Select(ext =>
            {
                var builtIn = IsBuiltIn(core, ext.Id);
                var folder = builtIn ? _builtIns[core.Profile.ProfilePath].ExtensionFolder
                    : _preferences.Sources.GetValueOrDefault(ext.Id) ?? _profileSources.GetValueOrDefault((profile, ext.Id));
                return (ext.Id, ext.Name, ext.IsEnabled, builtIn, folder, Missing: false);
            }).ToList();
            foreach (var source in _preferences.Sources.Where(source => native.All(ext => ext.Id != source.Key)))
                descriptors.Add((source.Key, _preferences.Names.GetValueOrDefault(source.Key) ?? System.IO.Path.GetFileName(source.Value),
                    false, false, source.Value, true));
            var pinned = _preferences.Pinned.ToHashSet(StringComparer.Ordinal);
            var items = await Task.Run(() => descriptors.Select(item =>
            {
                ExtensionManifestInfo? manifest = null;
                string? error = item.Missing ? "This extension is not loaded. Turn on Developer mode and reload it from its source folder." : null;
                try { if (item.folder is not null) manifest = ExtensionManifestReader.Read(item.folder); }
                catch (Exception ex) { error = ex.Message; }
                return new InstalledExtension(item.Id, item.Name, manifest?.Description ?? "", manifest?.Version ?? "",
                    item.IsEnabled, item.builtIn, pinned.Contains(item.Id), manifest?.IconDataUri ?? "",
                    manifest?.PopupPath is not null, manifest?.OptionsPath is not null, manifest?.Permissions ?? [],
                    item.builtIn ? "Built into umber" : item.folder is null ? "Installed in this profile" : "Loaded from a folder",
                    item.folder ?? "", error);
            }).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
            return new(DeveloperMode, items);
        }
        finally { _gate.Release(); }
    }

    public Task SetDeveloperModeAsync(bool enabled) => Mutate(async () =>
    {
        Save(_preferences with { DeveloperMode = enabled });
        await Task.CompletedTask;
    });

    public Task PinAsync(CoreWebView2 core, string id, bool pinned) => Mutate(async () =>
    {
        var extension = await FindAsync(core, id);
        if (pinned && !extension.IsEnabled) throw new InvalidOperationException("Enable the extension before pinning it.");
        var pins = new HashSet<string>(_preferences.Pinned, StringComparer.Ordinal);
        if (pinned && !pins.Contains(id) && pins.Count >= MaxPinnedExtensions)
            throw new InvalidOperationException("Up to three extensions can be pinned to the toolbar. Unpin one before adding another.");
        if (pinned) pins.Add(id); else pins.Remove(id);
        Save(_preferences with { Pinned = pins });
    });

    public Task LoadAsync(CoreWebView2 core, string folder) => Mutate(async () =>
    {
        RequireDeveloperMode();
        folder = System.IO.Path.GetFullPath(folder);
        if (_builtIns.TryGetValue(core.Profile.ProfilePath, out var builtIn) &&
            builtIn.ExtensionFolder is not null && AppPaths.PathEquals(folder, builtIn.ExtensionFolder))
            throw new InvalidOperationException("umber manages its built-in extension. Choose another folder.");
        await Task.Run(() => ExtensionManifestReader.Read(folder));
        var extension = await core.Profile.AddBrowserExtensionAsync(folder);
        var sources = new Dictionary<string, string>(_preferences.Sources) { [extension.Id] = folder };
        var names = new Dictionary<string, string>(_preferences.Names) { [extension.Id] = extension.Name };
        var enabled = new Dictionary<string, bool>(_preferences.Enabled) { [extension.Id] = extension.IsEnabled };
        SaveAfterNative(_preferences with { Sources = sources, Names = names, Enabled = enabled }, "The extension was loaded");
    });

    public Task ToggleAsync(CoreWebView2 core, string id, bool enabled) => Mutate(async () =>
    {
        ProtectComponent(id);
        if (IsBuiltIn(core, id)) throw new InvalidOperationException("Use the ad blocking preference to change this extension.");
        var extension = await FindAsync(core, id);
        await extension.EnableAsync(enabled);
        await WaitForStateAsync(core, id, enabled);
        SaveAfterNative(_preferences with { Enabled = new Dictionary<string, bool>(_preferences.Enabled) { [id] = enabled } }, "The extension state was changed");
    });

    public Task RemoveAsync(CoreWebView2 core, string id) => Mutate(async () =>
    {
        ProtectComponent(id);
        if (IsBuiltIn(core, id)) throw new InvalidOperationException("Built-in extensions can be disabled but cannot be removed.");
        var extension = (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(x => x.Id == id);
        if (extension is not null) await extension.RemoveAsync();
        else if (!_preferences.Sources.ContainsKey(id)) throw new InvalidOperationException("This extension is no longer installed.");
        var sources = new Dictionary<string, string>(_preferences.Sources);
        sources.Remove(id);
        var pins = new HashSet<string>(_preferences.Pinned); pins.Remove(id);
        var names = new Dictionary<string, string>(_preferences.Names); names.Remove(id);
        var enabled = new Dictionary<string, bool>(_preferences.Enabled); enabled.Remove(id);
        SaveAfterNative(_preferences with { Sources = sources, Pinned = pins, Names = names, Enabled = enabled }, "The extension was removed");
    });

    public Task ReloadAsync(CoreWebView2 core, string id) => Mutate(async () =>
    {
        RequireDeveloperMode();
        ProtectComponent(id);
        if (IsBuiltIn(core, id)) throw new InvalidOperationException("umber manages updates to its built-in extension.");
        var extension = (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(x => x.Id == id);
        var enabled = extension?.IsEnabled ?? _preferences.Enabled.GetValueOrDefault(id, true);
        var folder = Folder(core, id);
        await Task.Run(() => ExtensionManifestReader.Read(folder));
        var reloaded = await core.Profile.AddBrowserExtensionAsync(folder);
        await reloaded.EnableAsync(enabled);
        await WaitForStateAsync(core, reloaded.Id, enabled);
        var sources = new Dictionary<string, string>(_preferences.Sources); sources.Remove(id); sources[reloaded.Id] = folder;
        var names = new Dictionary<string, string>(_preferences.Names); names.Remove(id); names[reloaded.Id] = reloaded.Name;
        var states = new Dictionary<string, bool>(_preferences.Enabled); states.Remove(id); states[reloaded.Id] = enabled;
        var pins = new HashSet<string>(_preferences.Pinned); if (pins.Remove(id)) pins.Add(reloaded.Id);
        SaveAfterNative(_preferences with { Sources = sources, Names = names, Enabled = states, Pinned = pins }, "The extension was reloaded");
    });

    public async Task<Uri> GetPageUriAsync(CoreWebView2 core, string id, bool options)
    {
        var extension = await FindAsync(core, id);
        if (!extension.IsEnabled) throw new InvalidOperationException("This extension is disabled.");
        var folder = Folder(core, id);
        var manifest = await Task.Run(() => ExtensionManifestReader.Read(folder));
        var path = options ? manifest.OptionsPath : manifest.PopupPath;
        if (path is null) throw new InvalidOperationException("This extension does not provide that page.");
        return new Uri("chrome-extension://" + extension.Id + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
    }

    public bool IsBuiltIn(CoreWebView2 core, string id) => _builtIns.TryGetValue(core.Profile.ProfilePath, out var state) && state.ExtensionId == id;
    private string Folder(CoreWebView2 core, string id) => IsBuiltIn(core, id)
        ? _builtIns[core.Profile.ProfilePath].ExtensionFolder ?? throw new InvalidOperationException("The extension folder is unavailable.")
        : _preferences.Sources.GetValueOrDefault(id) ?? _profileSources.GetValueOrDefault((core.Profile.ProfilePath, id))
            ?? throw new InvalidOperationException("Load this extension's source folder through Developer mode first.");
    private void ProtectComponent(string id)
    { if (_components.Contains(id)) throw new InvalidOperationException("This extension is part of the browser engine and cannot be changed here."); }
    private static async Task<CoreWebView2BrowserExtension> FindAsync(CoreWebView2 core, string id) =>
        (await core.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidOperationException("This extension is no longer installed.");
    private static async Task WaitForStateAsync(CoreWebView2 core, string id, bool enabled)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if ((await FindAsync(core, id)).IsEnabled == enabled) return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("The browser has not confirmed the extension's enabled state. Try again.");
    }
    private void RequireDeveloperMode()
    { if (!DeveloperMode) throw new InvalidOperationException("Turn on Developer mode to load or reload extensions."); }
    private async Task Mutate(Func<Task> action)
    {
        await _gate.WaitAsync();
        try { await action(); }
        finally { _gate.Release(); Changed?.Invoke(); }
    }
    private void Save(Preferences candidate)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file)!);
        AtomicFileWriter.WriteAllText(_file, JsonSerializer.Serialize(candidate, JsonOptions));
        _preferences = candidate;
    }
    private void SaveAfterNative(Preferences candidate, string completed)
    {
        try { Save(candidate); }
        catch (Exception ex)
        {
            _preferences = candidate;
            throw new IOException(completed + ", but its umber preferences could not be saved. Keep this session open and try the action again. " + ex.Message, ex);
        }
    }
    public sealed record Preferences
    {
        public bool DeveloperMode { get; init; }
        public Dictionary<string, string> Sources { get; set; } = new();
        public HashSet<string> Pinned { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
        public Dictionary<string, bool> Enabled { get; set; } = new();
    }
}
