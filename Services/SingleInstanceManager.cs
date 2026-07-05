using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StrideBrowser.Services;

public static class SingleInstanceManager
{
    private const string MutexName = "StrideBrowser_SingleInstanceMutex_v1";
    private const string PipeName = "StrideBrowser_SingleInstancePipe_v1";
    
    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;

    public static event Action<string[]>? InstanceMessageReceived;

    /// <summary>
    /// Initializes single instance checking.
    /// Returns true if this is the primary instance.
    /// Returns false if another instance is already running (this instance should exit).
    /// </summary>
    public static bool Initialize(string[] args)
    {
        bool isFirstInstance;
        _mutex = new Mutex(true, MutexName, out isFirstInstance);

        if (isFirstInstance)
        {
            // We are the primary instance. Start listening for subsequent instances.
            _cts = new CancellationTokenSource();
            Task.Run(() => StartServerAsync(_cts.Token));
            return true;
        }
        else
        {
            // We are a secondary instance. Send arguments to the primary instance.
            SendArgumentsToPrimary(args);
            return false;
        }
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    private static async Task StartServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);

                using var reader = new StreamReader(server);
                var json = await reader.ReadToEndAsync(token);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var args = JsonSerializer.Deserialize<string[]>(json);
                        if (args != null && args.Length > 0)
                        {
                            InstanceMessageReceived?.Invoke(args);
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"NamedPipeServer error: {ex}");
                // Slight delay to prevent tight loop on error
                await Task.Delay(500, token);
            }
        }
    }

    private static void SendArgumentsToPrimary(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            // Wait up to 1 second for the server to become available
            client.Connect(1000);

            using var writer = new StreamWriter(client);
            var json = JsonSerializer.Serialize(args);
            writer.Write(json);
            writer.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"NamedPipeClient error: {ex}");
        }
    }
}
