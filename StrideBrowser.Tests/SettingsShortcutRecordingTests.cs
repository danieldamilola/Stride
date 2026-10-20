using System.Diagnostics;
using System.IO;
using System.Text.Json;
using StrideBrowser.Services.Input;
using Xunit;

namespace StrideBrowser.Tests;

/// <summary>
/// The Settings page records a shortcut from the key event and sends the combo to C#.
/// A recorder that reads e.key turns Ctrl+Shift+1 into Ctrl+Shift+!, which the shortcut
/// map rejects, so a supported default combination could not be typed back in.
/// The harness runs the shipped script over the default combinations and over physical
/// events whose shifted character differs from the physical key.
/// </summary>
public sealed class SettingsShortcutRecordingTests
{
    [Fact]
    public void RecordedCombos_MatchThePressedKeysAndParseInTheShortcutMap()
    {
        var root = RepoRoot();
        var harness = Path.Combine(root, "StrideBrowser.Tests", "settings-shortcut-recording.mjs");
        var script = Path.Combine(root, "Resources", "Scripts", "settings-page.js");
        Assert.True(File.Exists(harness), $"the shortcut recording harness is missing: {harness}");
        Assert.True(File.Exists(script), $"the settings script is missing: {script}");

        var defaults = ShortcutDefaults.All.Select(d => d.DefaultCombo).ToArray();
        var start = new ProcessStartInfo(YouTubeUnhookStorageDenialTests.ResolveNode())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(JsonSerializer.Serialize(defaults));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"node did not start: {start.FileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60000), "the shortcut recording harness did not finish");
        Assert.True(stdout.TrimStart().StartsWith('{'),
            $"the shortcut recording harness produced no report.\nstdout: {stdout}\nstderr: {stderr}");

        using var json = JsonDocument.Parse(stdout);
        var mismatches = json.RootElement.GetProperty("mismatches");
        Assert.True(mismatches.GetArrayLength() == 0,
            "the recorder changed a key the user pressed: " + mismatches.GetRawText());

        foreach (var entry in json.RootElement.GetProperty("report").EnumerateArray())
        {
            var combo = entry.GetProperty("actual").GetString()!;
            var expected = entry.GetProperty("expected").GetString()!;
            var source = entry.GetProperty("source").GetString()!;
            Assert.True(KeyboardShortcutMap.TryParseCombo(combo, out var modifiers, out var key),
                $"the recorder produced '{combo}' for the {source} case '{expected}', which the shortcut map cannot bind");
            Assert.Equal(combo, KeyboardShortcutMap.ToComboString(modifiers, key));
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stride.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
