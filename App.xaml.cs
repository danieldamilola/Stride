using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.ViewModels;

namespace StrideBrowser;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set AppUserModelID so Task Manager groups WebView2 processes under Stride
        Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID("StrideBrowser");

        // Create a Job Object so all child processes (WebView2, GPU, etc.)
        // appear grouped under Stride in Task Manager instead of "Utility"
        CreateProcessJob();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Build DI container
        _serviceProvider = Composition.BuildServiceProvider();

        // Create and show MainWindow via DI
        var vm = _serviceProvider.GetRequiredService<BrowserViewModel>();
        var window = new MainWindow(_serviceProvider, vm);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        Dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show(
                $"Stride Browser encountered an error:\n\n{e.Exception.Message}",
                "Stride Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception);
        e.SetObserved(); // Prevent process termination
    }

    private static void LogException(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(Helpers.AppPaths.CrashLogFile)!;
            Directory.CreateDirectory(dir);
            var logPath = Helpers.AppPaths.CrashLogFile;

            // Rotate log if it exceeds 1 MB
            try
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_048_576)
                    File.WriteAllText(logPath, ""); // Truncate
            }
            catch { /* Best-effort rotation */ }

            var entry = $"[{DateTime.UtcNow:O}] {ex}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Cannot let logging itself throw
        }
    }

    /// <summary>
    /// Creates a Windows Job Object and assigns the current process.
    /// All child processes (WebView2 browser, GPU, renderers, etc.)
    /// automatically inherit the job, causing Task Manager to group
    /// them under Stride instead of showing as separate "Utility" entries.
    /// </summary>
    private static void CreateProcessJob()
    {
        try
        {
            var job = Interop.NativeMethods.CreateJobObject(IntPtr.Zero, "StrideBrowserJob");
            if (job == IntPtr.Zero) return;

            var info = new Interop.NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags =
                Interop.NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            Interop.NativeMethods.SetInformationJobObject(
                job,
                Interop.NativeMethods.JobObjectExtendedLimitInformation,
                ref info,
                System.Runtime.InteropServices.Marshal.SizeOf(info));

            Interop.NativeMethods.AssignProcessToJobObject(
                job, Interop.NativeMethods.GetCurrentProcess());
        }
        catch
        {
            // Non-critical — Task Manager grouping is cosmetic
        }
    }
}
