using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Stride.Updater;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 2)
            return;

        string updateZipPath = args[0];
        string targetDir = args[1];

        // Wait for Stride and WebView2 processes to exit
        for (int i = 0; i < 30; i++)
        {
            var strideProcesses = Process.GetProcessesByName("Stride");
            var edgeProcesses = Process.GetProcessesByName("msedgewebview2");
            var ourProcessId = Process.GetCurrentProcess().Id;

            // Only care about processes in our target directory (to avoid killing other Edge instances)
            bool isAnyRunning = false;
            
            foreach (var p in strideProcesses)
            {
                if (p.Id == ourProcessId) continue;
                try { if (p.MainModule?.FileName.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase) == true) isAnyRunning = true; } catch (System.Exception ex) { System.Console.WriteLine(ex); }
            }
            
            foreach (var p in edgeProcesses)
            {
                try { if (p.MainModule?.FileName.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase) == true) isAnyRunning = true; } catch (System.Exception ex) { System.Console.WriteLine(ex); }
            }

            if (!isAnyRunning)
                break;

            Thread.Sleep(500);
        }

        // Failsafe kill
        try { foreach (var p in Process.GetProcessesByName("Stride")) { if (p.Id != Process.GetCurrentProcess().Id) p.Kill(); } } catch (System.Exception ex) { System.Console.WriteLine(ex); }

        try
        {
            string backupDir = Path.Combine(targetDir, "Backup");
            string stagingDir = Path.Combine(targetDir, "UpdateStaging");

            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);

            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(updateZipPath, stagingDir, overwriteFiles: true);

            Directory.CreateDirectory(backupDir);

            // Move current files to backup
            foreach (var file in Directory.GetFiles(targetDir))
            {
                var fileName = Path.GetFileName(file);
                // Don't move the updater itself while it's running!
                if (fileName.Equals("Stride.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Stride.Updater.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Stride.Updater.pdb", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("updater_error.log", StringComparison.OrdinalIgnoreCase))
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
                File.Move(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(stagingDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                if (!Directory.Exists(dest))
                    Directory.Move(dir, dest);
            }

            // Cleanup staging and zip
            Directory.Delete(stagingDir, true);
            File.Delete(updateZipPath);

            // Launch new app
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(targetDir, "Stride.exe"),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(targetDir, "updater_error.log"), ex.ToString());
        }
    }
}
