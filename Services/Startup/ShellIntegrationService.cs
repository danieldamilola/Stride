using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace StrideBrowser.Services.Startup;

public interface IShellIntegrationService
{
    void EnsureShellIntegration(bool isPostUpdate, bool isNewVersion);
}

public static class ShellIntegrationPolicy
{
    public const string AppId = "Stride";

    public static bool ShouldRefresh(bool isPostUpdate, bool isNewVersion)
    {
        return isPostUpdate || isNewVersion;
    }

    public static bool NeedsRepair(string? currentAppId)
    {
        return !string.Equals(currentAppId, AppId, StringComparison.Ordinal);
    }

    public static string GetStartMenuShortcutPath(string startMenuProgramsDir)
    {
        return Path.Combine(startMenuProgramsDir, "Stride", "Stride.lnk");
    }

    public static string GetTaskbarPinnedShortcutPath(string appDataDir)
    {
        return Path.Combine(appDataDir, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", "Stride.lnk");
    }
}

public sealed class ShellIntegrationService : IShellIntegrationService
{
    public void EnsureShellIntegration(bool isPostUpdate, bool isNewVersion)
    {
        try
        {
            bool repaired = VerifyShortcutAppIds();
            if (ShellIntegrationPolicy.ShouldRefresh(isPostUpdate, isNewVersion) || repaired)
            {
                try
                {
                    Interop.NativeMethods.SHChangeNotify(
                        Interop.NativeMethods.SHCNE_ASSOCCHANGED,
                        Interop.NativeMethods.SHCNF_IDLIST,
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
                catch (Exception ex) { Trace.WriteLine($"Shell notify failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"Shell integration failed: {ex.Message}"); }
    }

    private static bool VerifyShortcutAppIds()
    {
        bool repaired = false;

        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrEmpty(startMenu))
            repaired |= EnsureShortcutAppId(ShellIntegrationPolicy.GetStartMenuShortcutPath(startMenu));

        if (!string.IsNullOrEmpty(appData))
            repaired |= EnsureShortcutAppId(ShellIntegrationPolicy.GetTaskbarPinnedShortcutPath(appData));

        return repaired;
    }

    private static bool EnsureShortcutAppId(string shortcutPath)
    {
        try
        {
            if (!File.Exists(shortcutPath))
                return false;
            if (!ShortcutAppIdRepair.TryGetAppId(shortcutPath, out string? currentAppId))
                return ShortcutAppIdRepair.TrySetAppId(shortcutPath, ShellIntegrationPolicy.AppId);
            if (ShellIntegrationPolicy.NeedsRepair(currentAppId))
                return ShortcutAppIdRepair.TrySetAppId(shortcutPath, ShellIntegrationPolicy.AppId);
            return false;
        }
        catch
        {
            return false;
        }
    }
}

internal static class ShortcutAppIdRepair
{
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid PKEY_AppUserModel_ID = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint PID_AppUserModel_ID = 5;
    private const ushort VT_LPWSTR = 31;
    private const uint STGM_READ = 0;
    private const uint STGM_READWRITE = 2;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    internal static bool TryGetAppId(string shortcutPath, out string? appId)
    {
        appId = null;
        object? link = null;
        try
        {
            if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath))
                return false;

            var linkType = Type.GetTypeFromCLSID(CLSID_ShellLink);
            if (linkType is null)
                return false;
            link = Activator.CreateInstance(linkType);
            if (link is null)
                return false;

            var persistFile = (IPersistFile)link;
            persistFile.Load(shortcutPath, STGM_READ);

            var store = (IPropertyStore)link;
            var key = new PROPERTYKEY { fmtid = PKEY_AppUserModel_ID, pid = PID_AppUserModel_ID };
            int hr = store.GetValue(ref key, out PROPVARIANT pv);
            if (hr != 0)
                return false;

            try
            {
                if (pv.vt != VT_LPWSTR || pv.pszVal == IntPtr.Zero)
                    return false;
                appId = Marshal.PtrToStringUni(pv.pszVal);
                return appId is not null;
            }
            finally
            {
                try { PropVariantClear(ref pv); } catch { }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
            {
                try { Marshal.ReleaseComObject(link); } catch { }
            }
        }
    }

    internal static bool TrySetAppId(string shortcutPath, string appId)
    {
        try
        {
            if (string.IsNullOrEmpty(shortcutPath) || string.IsNullOrEmpty(appId))
                return false;
            if (!File.Exists(shortcutPath))
                return false;

            var linkType = Type.GetTypeFromCLSID(CLSID_ShellLink);
            if (linkType is null)
                return false;
            var link = Activator.CreateInstance(linkType);
            if (link is null)
                return false;
            try
            {
                var persistFile = (IPersistFile)link;
                persistFile.Load(shortcutPath, STGM_READWRITE);

                var store = (IPropertyStore)link;
                var key = new PROPERTYKEY { fmtid = PKEY_AppUserModel_ID, pid = PID_AppUserModel_ID };

                var pv = new PROPVARIANT();
                pv.vt = VT_LPWSTR;
                pv.pszVal = Marshal.StringToCoTaskMemUni(appId);
                try
                {
                    int hr = store.SetValue(ref key, ref pv);
                    if (hr != 0)
                        return false;
                    hr = store.Commit();
                    if (hr != 0)
                        return false;
                    persistFile.Save(shortcutPath, true);
                    return true;
                }
                finally
                {
                    if (pv.pszVal != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pv.pszVal);
                        pv.pszVal = IntPtr.Zero;
                    }
                    if (link is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                    else if (Marshal.IsComObject(link))
                    {
                        try { Marshal.ReleaseComObject(link); } catch { }
                    }
                }
            }
            catch
            {
                if (Marshal.IsComObject(link))
                {
                    try { Marshal.ReleaseComObject(link); } catch { }
                }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pszVal;
    }

    [ComImport]
    [Guid("0000010C-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }
}
