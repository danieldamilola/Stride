using System;
using System.Diagnostics;
using System.IO;

namespace StrideBrowser.Services.Startup;

public sealed class UpdaterRecoveryService
{
    public void TryApplyPendingUpdater()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var pending in Directory.GetFiles(baseDir, "Stride.Updater*.new"))
            {
                var target = pending.Substring(0, pending.Length - 4);
                try
                {
                    File.Move(pending, target, true);
                    Trace.WriteLine($"Applied pending updater {Path.GetFileName(pending)}");
                }
                catch (Exception ex) { Trace.WriteLine($"Failed to apply pending updater {pending}: {ex.Message}"); }
            }
        }
        catch { }
    }

    public void TryCleanStaleUpdaterArtifacts()
    {
        try
        {
            var staging = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateStaging");
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch { }
                Trace.WriteLine("Cleaned stale UpdateStaging");
            }
        }
        catch { }
    }

    public void Run()
    {
        TryApplyPendingUpdater();
        TryCleanStaleUpdaterArtifacts();
    }
}
