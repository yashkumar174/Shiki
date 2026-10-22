using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConvertedBro.Core;

namespace ConvertedBro.Shell;

/// <summary>
/// Keeps one ConvertedBro process per user. Explorer starts a new process for every selected
/// file; those hand their arguments to the first one over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    static readonly string Id = $"ConvertedBro-{Environment.UserName}";
    readonly Mutex _mutex;
    readonly CancellationTokenSource _stop = new();

    public event Action<string[]>? MessageReceived;

    public bool IsPrimary { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{Id}", out bool createdNew);
        IsPrimary = createdNew;
    }

    public void StartServer() => _ = Task.Run(ServerLoop);

    async Task ServerLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(Id, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                // Read on the side so the next Explorer launch can connect straight away.
                _ = ReadMessage(server);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                Log.Error("Pipe server error", ex);
            }
        }
    }

    async Task ReadMessage(NamedPipeServerStream server)
    {
        try
        {
            using (server)
            using (var reader = new StreamReader(server))
            {
                var args = JsonSerializer.Deserialize<string[]>(await reader.ReadToEndAsync(_stop.Token));
                if (args != null) MessageReceived?.Invoke(args);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("Could not read message from another instance", ex);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Sends args to the primary instance. Returns false if it could not be reached.</summary>
    public static bool SendToPrimary(string[] args)
    {
        // The primary may still be starting up (several Explorer launches at once), so retry briefly.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(250);

                // We were launched by Explorer, so we may bring windows forward; pass that right on.
                if (NativeMethods.GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var pid))
                    NativeMethods.AllowSetForegroundWindow((int)pid);

                using var writer = new StreamWriter(client);
                writer.Write(JsonSerializer.Serialize(args));
                writer.Flush();
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Thread.Sleep(150);
            }
        }
        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
