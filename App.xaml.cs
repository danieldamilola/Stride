using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;

namespace StrideBrowser;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceManager.Initialize(e.Args))
        {
            Shutdown();
            return;
        }

        // Register crash handlers once, as early as possible — before anything else can throw.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Trace log lives under %LocalAppData%\StrideBrowser — never repo/working-directory local.
        Directory.CreateDirectory(Path.GetDirectoryName(Helpers.AppPaths.LogFile)!);
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Helpers.AppPaths.LogFile));
        System.Diagnostics.Trace.AutoFlush = true;
        System.Diagnostics.Trace.WriteLine("--- STRIDE STARTED ---");

        base.OnStartup(e);

        // Set AppUserModelID for taskbar grouping
        Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID("Stride");

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
        SingleInstanceManager.Shutdown();
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
}
