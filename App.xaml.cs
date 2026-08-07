using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Services;
using StrideBrowser.ViewModels;
using Velopack;

namespace StrideBrowser;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    public IServiceProvider Services => _serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();

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

        CheckForPreviousCrash();

        // Create and show MainWindow via DI
        var vm = _serviceProvider.GetRequiredService<BrowserViewModel>();
        
        var window = new MainWindow(_serviceProvider, vm);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstanceManager.Shutdown();
        base.OnExit(e);
    }

    private void CheckForPreviousCrash()
    {
        try
        {
            var logPath = Helpers.AppPaths.CrashLogFile;
            if (File.Exists(logPath))
            {
                var crashDetails = File.ReadAllText(logPath);
                if (!string.IsNullOrWhiteSpace(crashDetails))
                {
                    var result = MessageBox.Show(
                        "Stride Browser recovered from an unexpected error during your last session.\n\nWould you like to report this to the developer on GitHub?",
                        "Crash Recovered",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Take the last 2000 chars to avoid URL too long issues
                        var trimmedLog = crashDetails.Length > 2000 ? crashDetails.Substring(crashDetails.Length - 2000) : crashDetails;
                        var body = Uri.EscapeDataString($"**Describe the bug**\n\n**Crash Log:**\n```\n{trimmedLog}\n```");
                        var url = $"https://github.com/danieldamilola/Stride/issues/new?title=Crash%20Report&body={body}";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                }
                
                File.Delete(logPath);
            }
        }
        catch { }
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
