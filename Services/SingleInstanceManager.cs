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
    private static bool _ownsMutex;

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
        _ownsMutex = isFirstInstance;

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
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ReleaseMutex error: {ex}"); }
        }
        _mutex?.Dispose();
    }

    private static async Task StartServerAsync(CancellationToken token)
    {
#pragma warning disable CA1416 // Validate platform compatibility
        var ps = new System.IO.Pipes.PipeSecurity();
        var id = System.Security.Principal.WindowsIdentity.GetCurrent().Owner;
        if (id != null)
        {
            ps.AddAccessRule(new System.IO.Pipes.PipeAccessRule(id, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        }
#pragma warning restore CA1416

        while (!token.IsCancellationRequested)
        {
            try
            {
#pragma warning disable CA1416
                using var server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, ps);
#pragma warning restore CA1416
                await server.WaitForConnectionAsync(token);

                using var reader = new StreamReader(server);
                string? json;
                try
                {
                    json = await ReadToEndAsync(reader, token);
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                    continue;

                string[]? args = null;
                try
                {
                    args = JsonSerializer.Deserialize<string[]>(json);
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SingleInstance bad payload: {ex.Message}");
                    continue;
                }

                if (args is null || args.Length == 0 || args.Length > 32)
                    continue;

                // Each arg must look like a URL or a command-line token.
                // Reject anything that contains control characters, embedded
                // newlines, or extreme length that could be used to drive a
                // downstream parser into a bad state.
                if (!args.All(IsSafeArg))
                    continue;

                InstanceMessageReceived?.Invoke(args);
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

    /// <summary>
    /// Rejects pipe payloads whose strings contain control characters or
    /// exceed the per-argument size cap. The browser accepts argv as URLs
    /// or flags, neither of which should ever contain tabs, newlines, or
    /// non-printable bytes.
    /// </summary>
    private static bool IsSafeArg(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 2048)
            return false;
        foreach (var c in s)
        {
            if (c < 0x20 || c == 0x7F)
                return false;
        }
        return true;
    }

    private static async Task<string> ReadToEndAsync(StreamReader reader, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var buffer = await reader.ReadToEndAsync(cts.Token);
        return buffer;
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
