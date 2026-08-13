using System;
using Velopack;

namespace StrideBrowser;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Run Velopack first, before ANY WPF components or Application objects are initialized!
        // This is critical because the installer process will run Stride.exe --velopack-install
        // and expects it to exit immediately without spinning up UI threads or crashing.
        VelopackApp.Build().Run();

        // If Velopack handled a command (e.g., install/update/uninstall), it exits the process before this line.
        // If we reach here, it's a normal launch.
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
