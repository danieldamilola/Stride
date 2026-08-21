using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrideBrowser.Services.Input;

/// <summary>
/// Groups all action delegates that keyboard shortcuts can invoke.
/// Replaces the 17-parameter constructor of KeyboardShortcutMap.
/// </summary>
public sealed record ShortcutActions
{
    public required Func<Task> FocusAddressBar { get; init; }
    public required Func<Task> SaveAllTabs { get; init; }
    public required Func<bool, Task> CycleTab { get; init; }
    public required Func<Task> ToggleFullscreen { get; init; }
    public required Func<bool> IsFullscreen { get; init; }
    public required Func<Task> OpenHistory { get; init; }
    public required Func<Task> OpenDownloads { get; init; }
    public required Func<int, Task> SwitchToTabIndex { get; init; }
    public required Action<string> CopyUrl { get; init; }
    public required Func<List<(string url, string title)>> SendAllToOneTab { get; init; }
    public required Action<List<(string url, string title)>> SaveOneTabGroup { get; init; }
    public required Action SyncTabsBinding { get; init; }
    public required Func<Task> OpenOneTab { get; init; }
    public required Func<Task> OpenSettings { get; init; }
    public required Func<Task> LaunchTCLens { get; init; }
    public required Func<Task> ToggleReader { get; init; }
}
