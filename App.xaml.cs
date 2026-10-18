using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StrideBrowser.Services;

namespace StrideBrowser;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    public IServiceProvider Services => _serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceManager.Initialize(e.Args))
        {
            Shutdown();
            return;
        }

        // Register crash handlers once, as early as possible - before anything else can throw.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        new Services.Startup.UpdaterRecoveryService().Run();

        // Ensure all data directories exist: base, favicon cache, extensions, WebView2, focus cache.
        Helpers.AppPaths.EnsureDirectories();

        // Rotate log if it exceeds 1 MB
        try
        {
            var logPath = Helpers.AppPaths.LogFile;
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_048_576)
                File.WriteAllText(logPath, ""); // Truncate
        }
        catch { /* Best-effort rotation */ }

        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Helpers.AppPaths.LogFile));
        System.Diagnostics.Trace.AutoFlush = true;
        System.Diagnostics.Trace.WriteLine("--- STRIDE STARTED ---");

        base.OnStartup(e);

        // Set AppUserModelID for taskbar grouping
        Interop.NativeMethods.SetCurrentProcessExplicitAppUserModelID("Stride");

        // Build DI container
        _serviceProvider = Composition.BuildServiceProvider();

        // uBlock is not prepared here. An earlier revision fired a background
        // EnsureUBlockDownloadedAsync from startup so the folder would exist before
        // the first WebView, which meant two writers against the same folder: this one
        // and the one inside the extension startup coordinator, which can rewrite
        // assets.json while the platform has that folder registered. One coordinator
        // owns preparation and install per profile now. It runs on a pool thread
        // before the first external navigation, and its result is read back from the
        // profile rather than assumed, so nothing is lost by not guessing here.

        // Check for updates silently if enabled
        var settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
        var settings = settingsStore.Load();
        
        var updateService = _serviceProvider.GetRequiredService<UpdateService>();
        updateService.AppExitRequested += () => Dispatcher.Invoke(() => Shutdown());
        
        if (settings.AutoCheckForUpdates)
        {
            // Fire and forget the async silent check
            _ = updateService.CheckForUpdatesQuietlyAsync();
        }

        CheckForPreviousCrash();

        // Create and show MainWindow via DI. The manager owns every window's scope, and
        // this one is the startup window: it restores the session and owns the command
        // line. Windows torn off later get a scope of their own with no restore.
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var window = _serviceProvider.GetRequiredService<Services.UI.BrowserWindowManager>().CreateWindow(restoreStartupSession: true);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { System.Diagnostics.Trace.WriteLine($"App OnExit code={e.ApplicationExitCode}"); System.Diagnostics.Trace.Flush(); }
        catch { }
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
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to check for previous crash: {ex}");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        try { System.Diagnostics.Trace.WriteLine($"DispatcherUnhandled: {e.Exception}"); System.Diagnostics.Trace.Flush(); }
        catch { }
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
        {
            LogException(ex);
            try { System.Diagnostics.Trace.WriteLine($"AppDomainUnhandled terminating={e.IsTerminating}: {ex}"); System.Diagnostics.Trace.Flush(); }
            catch { }
        }
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

