using System;
using System.IO;

namespace UmberBrowser.Helpers;

/// <summary>
/// Provides standard paths for Umber's data files.
/// All files are stored in %LocalAppData%\UmberBrowser.
/// Set the UMBER_DATA_DIR environment variable to point the whole data tree
/// somewhere else. That is how the recovery verification harness runs the real
/// app against an isolated profile without touching the installed browser's data.
/// </summary>
public static class AppPaths
{
    /// <summary>Environment variable that relocates the whole data tree.</summary>
    public const string DataDirectoryOverrideVariable = "UMBER_DATA_DIR";

    private const string ProductionDataFolderName = "UmberBrowser";

    private static readonly string Base = ResolveDataDirectory(
        Environment.GetEnvironmentVariable(DataDirectoryOverrideVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string DataDirectory => Base;

    /// <summary>
    /// True when an override actually redirected the data tree. Single instance
    /// names are scoped on this so a relocated run never takes over the
    /// installed browser's mutex and never forwards arguments to it.
    /// </summary>
    public static bool IsIsolatedProfile => !PathEquals(Base, ProductionDataDirectory);

    /// <summary>The path used when no override is set.</summary>
    public static string ProductionDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductionDataFolderName);

    public static string SettingsFile => Path.Combine(Base, "settings.json");
    public static string HistoryFile => Path.Combine(Base, "history.json");
    public static string OneTabFile => Path.Combine(Base, "onetab.json");
    public static string SessionFile => Path.Combine(Base, "session.json");
    public static string ClosedTabsFile => Path.Combine(Base, "closed-tabs.json");
    public static string DownloadsFile => Path.Combine(Base, "downloads.json");
    public static string FaviconCacheDir => Path.Combine(Base, "favicons");
    public static string ExtensionsDir => Path.Combine(Base, "extensions");
    public static string WebViewDataDir => Path.Combine(Base, "WebView2");
    public static string WebView2Dir => WebViewDataDir;
    public static string LogFile => Path.Combine(Base, "umber.log");
    public static string CrashLogFile => Path.Combine(Base, "crash.log");
    public static string UBlockHashFile => Path.Combine(Base, "ublock.sha256");
    public static string FocusCacheDir => Path.Combine(Base, "focus_cache");

    /// <summary>
    /// Picks the data directory from an override value. Anything that is not a
    /// usable, separate directory falls back to the production path. The rules
    /// exist so a mistyped override can never write into the real profile.
    /// Pure logic, covered by unit tests.
    /// </summary>
    public static string ResolveDataDirectory(string? overrideValue, string localApplicationDataPath)
    {
        var production = NormalizeDirectoryPath(Path.Combine(localApplicationDataPath, ProductionDataFolderName));
        if (string.IsNullOrWhiteSpace(overrideValue)) return production;

        var candidateText = overrideValue.Trim().Trim('"').Trim();
        if (candidateText.Length == 0) return production;

        // A relative override depends on the current directory, which differs
        // between a shortcut, a shell and a test host. Refuse to guess.
        if (!Path.IsPathFullyQualified(candidateText)) return production;

        string candidate;
        try { candidate = NormalizeDirectoryPath(candidateText); }
        catch (Exception) { return production; }

        // Same place as the real profile, or a file, not a directory.
        if (PathEquals(candidate, production)) return production;
        if (File.Exists(candidate)) return production;

        // An override that contains the real profile would scatter Umber's files
        // next to it and make "delete the profile" ambiguous. Refuse.
        if (IsSameOrAncestor(candidate, production)) return production;

        return candidate;
    }

    /// <summary>
    /// Absolute path with no trailing separator, so "C:\a\" and "C:\a" name the
    /// same directory. A bare drive root keeps its separator.
    /// </summary>
    public static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && PathEquals(full, root)) return root;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Case insensitive comparison of two fully qualified paths.</summary>
    public static bool PathEquals(string left, string right) =>
        string.Equals(left.TrimEnd(Path.DirectorySeparatorChar),
                      right.TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="ancestor"/> is the same path as <paramref name="child"/> or contains it.</summary>
    public static bool IsSameOrAncestor(string ancestor, string child)
    {
        var a = ancestor.TrimEnd(Path.DirectorySeparatorChar);
        var c = child.TrimEnd(Path.DirectorySeparatorChar);
        if (PathEquals(a, c)) return true;
        return c.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ensures the data directory exists.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(FaviconCacheDir);
        if (!Directory.Exists(ExtensionsDir)) Directory.CreateDirectory(ExtensionsDir);
        if (!Directory.Exists(WebViewDataDir)) Directory.CreateDirectory(WebViewDataDir);
        if (!Directory.Exists(FocusCacheDir)) Directory.CreateDirectory(FocusCacheDir);
    }
}