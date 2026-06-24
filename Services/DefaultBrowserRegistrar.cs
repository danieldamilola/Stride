using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace StrideBrowser.Services;

/// <summary>
/// Registers Stride Browser as a default browser candidate in the Windows registry (HKCU).
/// On Windows 10+, apps cannot programmatically set themselves as default — the user must
/// choose in Settings → Apps → Default Apps after registration.
/// </summary>
public static class DefaultBrowserRegistrar
{
    private const string AppName = "Stride Browser";
    private const string AppDescription = "A fast, privacy-focused web browser.";
    private const string ProgIdHtml = "StrideBrowserHTML";
    private const string ProgIdUrl = "StrideBrowserURL";
    private const string CapabilitiesPath = @"Software\StrideBrowser\Capabilities";

    /// <summary>
    /// Writes all required registry keys under HKCU so Windows recognises Stride Browser
    /// as an available browser for HTTP, HTTPS, and HTML files.
    /// </summary>
    public static void Register()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StrideBrowser.exe");
            var openCommand = $"\"{exePath}\" \"%1\"";
            var iconValue = $"\"{exePath}\",0";

            // ── ProgID for HTML files ──
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgIdHtml}"))
            {
                key.SetValue("", $"{AppName} HTML Document");
                using var iconKey = key.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", iconValue);
                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", openCommand);
            }

            // ── ProgID for URL protocols ──
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgIdUrl}"))
            {
                key.SetValue("", $"{AppName} URL");
                key.SetValue("URL Protocol", "");
                using var iconKey = key.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", iconValue);
                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", openCommand);
            }

            // ── Application Capabilities ──
            using (var key = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                key.SetValue("ApplicationName", AppName);
                key.SetValue("ApplicationDescription", AppDescription);
                key.SetValue("ApplicationIcon", iconValue);

                using var urlAssoc = key.CreateSubKey("URLAssociations");
                urlAssoc.SetValue("http", ProgIdUrl);
                urlAssoc.SetValue("https", ProgIdUrl);

                using var fileAssoc = key.CreateSubKey("FileAssociations");
                fileAssoc.SetValue(".htm", ProgIdHtml);
                fileAssoc.SetValue(".html", ProgIdHtml);
                fileAssoc.SetValue(".xhtml", ProgIdHtml);
                fileAssoc.SetValue(".shtml", ProgIdHtml);

                using var startMenu = key.CreateSubKey("StartMenu");
                startMenu.SetValue("StartMenuInternet", AppName);
            }

            // ── RegisteredApplications (tells Windows this app exists as a browser) ──
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                key.SetValue(AppName, CapabilitiesPath);
            }

            // ── StartMenuInternet registration (appears in "Set Default Programs") ──
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Clients\StartMenuInternet\{AppName}"))
            {
                key.SetValue("", AppName);
                using var caps = key.CreateSubKey("Capabilities");
                caps.SetValue("ApplicationName", AppName);
                caps.SetValue("ApplicationDescription", AppDescription);
                caps.SetValue("ApplicationIcon", iconValue);

                using var urlAssoc = caps.CreateSubKey("URLAssociations");
                urlAssoc.SetValue("http", ProgIdUrl);
                urlAssoc.SetValue("https", ProgIdUrl);

                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", openCommand);
            }

            Trace.WriteLine("DefaultBrowserRegistrar: registration complete.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DefaultBrowserRegistrar.Register failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the Windows Settings → Default Apps page so the user can select Stride Browser.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DefaultBrowserRegistrar.OpenSettings failed: {ex.Message}");
        }
    }

    /// <summary>Checks whether Stride Browser is already registered as a browser candidate.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            return key?.GetValue(AppName) is not null;
        }
        catch { return false; }
    }
}
