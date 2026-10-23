using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Umber.Updater;

public static class Program
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void Main(string[] args)
    {
        if (args.Length < 2)
            return;

        string updateZipPath = args[0];
        string targetDir = args[1];

        // Wait for Umber processes to exit - WebView2 hosts are children of Umber
        // and will exit shortly after Umber exits. We do not filter WebView2 by path
        // because its exe lives in the Edge install, not targetDir.
        for (int i = 0; i < 30; i++)
        {
            var umberProcesses = Process.GetProcessesByName("Umber");
            var ourProcessId = Process.GetCurrentProcess().Id;

            bool isAnyRunning = false;
            
            foreach (var p in umberProcesses)
            {
                if (p.Id == ourProcessId) continue;
                try
                {
                    var fileName = p.MainModule?.FileName;
                    if (IsInTargetDirectory(fileName, targetDir))
                        isAnyRunning = true;
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine(ex);
                    // Ignore processes we can't inspect (e.g. ghost/elevated/other users)
                }
                finally { try { p.Dispose(); } catch { } }
            }

            if (!isAnyRunning)
                break;

            Thread.Sleep(500);
        }

        // Grace period for WebView2 child processes to release file locks
        Thread.Sleep(1000);

        // Failsafe wait (abort instead of indiscriminate kill)
        bool anyRemaining = false;
        try
        {
            foreach (var p in Process.GetProcessesByName("Umber"))
            {
                if (p.Id == Process.GetCurrentProcess().Id) { try { p.Dispose(); } catch { } continue; }
                try
                {
                    var fileName = p.MainModule?.FileName;
                    if (IsInTargetDirectory(fileName, targetDir))
                        anyRemaining = true;
                }
                catch (System.Exception ex) { System.Console.WriteLine(ex); }
                finally { try { p.Dispose(); } catch { } }
            }
        } 
        catch (System.Exception ex) { System.Console.WriteLine(ex); }

        if (anyRemaining)
        {
            File.WriteAllText(Path.Combine(targetDir, "updater_error.log"), "Update aborted: Umber processes did not exit.");
            return;
        }

        string backupDir = Path.Combine(targetDir, "Backup");
        string stagingDir = Path.Combine(targetDir, "UpdateStaging");
        
        try
        {
            // Convergent cleanup: remove stale staging from previous crash before extracting.
            // Do NOT delete Backup yet - it holds the last good version for rollback.
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);

            if (!File.Exists(updateZipPath))
                throw new FileNotFoundException($"Update package not found: {updateZipPath}");

            Directory.CreateDirectory(stagingDir);

            // Screen the archive before extracting. A small zip can declare an enormous output,
            // and this process replaces every file in the install directory with the result.
            ScreenArchive(updateZipPath);

            ExtractArchiveWithLimits(updateZipPath, stagingDir);

            // Validate extracted package contains the main executable
            if (!File.Exists(Path.Combine(stagingDir, "Umber.exe")))
                throw new InvalidDataException("Extracted package does not contain Umber.exe - aborting update");

            // Only after staging is verified do we replace the previous backup.
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            Directory.CreateDirectory(backupDir);

            // Move current files to backup
            foreach (var file in Directory.GetFiles(targetDir))
            {
                var fileName = Path.GetFileName(file);
                // Don't move the updater itself while it's running!
                if (fileName.Equals("Umber.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Umber.Updater.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Umber.Updater.pdb", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("updater_error.log", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("post_update.flag", StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Move(file, Path.Combine(backupDir, fileName), true);
            }

            foreach (var dir in Directory.GetDirectories(targetDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.Equals("Backup", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("UpdateStaging", StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.Move(dir, Path.Combine(backupDir, dirName));
            }

            // Move new files from staging to target
            foreach (var file in Directory.GetFiles(stagingDir))
            {
                var fileName = Path.GetFileName(file);
                bool isUpdater = fileName.StartsWith("Umber.Updater", StringComparison.OrdinalIgnoreCase);
                if (isUpdater)
                {
                    // Cannot overwrite running updater - stage as .new for next launch to pick up
                    var pendingPath = Path.Combine(targetDir, fileName + ".new");
                    File.Copy(file, pendingPath, true);
                    continue;
                }
                    
                File.Move(file, Path.Combine(targetDir, fileName), true);
            }

            foreach (var dir in Directory.GetDirectories(stagingDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                MoveOrMergeDirectory(dir, dest);
            }

            // Cleanup staging and zip
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            File.Delete(updateZipPath);

            try
            {
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }

            // Launch new app
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(targetDir, "Umber.exe"),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Restore from backup if update failed - merge to handle partial moves
            if (Directory.Exists(backupDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(backupDir))
                    {
                        File.Move(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
                    }
                    foreach (var dir in Directory.GetDirectories(backupDir))
                    {
                        var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                        MoveOrMergeDirectory(dir, dest);
                    }
                } 
                catch (Exception restoreEx) 
                {
                    ex = new AggregateException(ex, restoreEx);
                }
            }
            File.WriteAllText(Path.Combine(targetDir, "updater_error.log"), ex.ToString());
        }
    }

    /// <summary>Ceilings for an update package, all well above a real Umber release.</summary>
    private const int MaxArchiveEntries = 20000;
    private const long MaxArchiveUncompressedBytes = 1024L * 1024 * 1024;
    private const long MaxSingleEntryBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Refuses an archive whose declared shape is implausible before anything is written to
    /// disk. Entry sizes are read from the central directory, which a decompression bomb
    /// inflates without spending bytes on the wire.
    /// </summary>
    private static void ScreenArchive(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        if (zip.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException($"Update archive has {zip.Entries.Count} entries, over the {MaxArchiveEntries} limit.");

        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > MaxSingleEntryBytes)
                throw new InvalidDataException($"Update archive entry '{entry.FullName}' declares {entry.Length} bytes.");

            total += entry.Length;
            if (total > MaxArchiveUncompressedBytes)
                throw new InvalidDataException($"Update archive declares more than {MaxArchiveUncompressedBytes} bytes of output.");
        }
    }

    /// <summary>
    /// Extracts the archive while counting bytes actually written. Central-directory sizes can
    /// understate the output, so per-entry and total caps are enforced on the copy loop.
    /// </summary>
    private static void ExtractArchiveWithLimits(string zipPath, string stagingDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var stagingRoot = Path.GetFullPath(stagingDir);
        long totalWritten = 0;
        var buffer = new byte[81920];

        foreach (var entry in zip.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
            if (!destination.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination, stagingRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Update archive entry escapes staging: '{entry.FullName}'.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            long entryWritten = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                entryWritten += read;
                totalWritten += read;
                if (entryWritten > MaxSingleEntryBytes)
                    throw new InvalidDataException($"Update archive entry '{entry.FullName}' exceeds {MaxSingleEntryBytes} bytes written.");
                if (totalWritten > MaxArchiveUncompressedBytes)
                    throw new InvalidDataException($"Update archive exceeds {MaxArchiveUncompressedBytes} bytes written.");
                dest.Write(buffer, 0, read);
            }
        }
    }

    private static bool IsInTargetDirectory(string? fileName, string targetDir)
    {
        if (string.IsNullOrEmpty(fileName)) return true;
        try
        {
            var fileDir = Path.GetDirectoryName(fileName);
            if (string.IsNullOrEmpty(fileDir)) return true;
            var normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFileDir = Path.GetFullPath(fileDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedFileDir, normalizedTarget, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static void MoveOrMergeDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir))
        {
            Directory.Move(sourceDir, destDir);
            return;
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Move(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            MoveOrMergeDirectory(subDir, destSubDir);
        }

        Directory.Delete(sourceDir, true);
    }
}
