using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using FrameCore.Core.Platform;

namespace FrameCore.Core.Elevation;

/// <summary>
/// Elevated side. Connects to the UI's pipe, verifies that the server is its parent process running the
/// same executable, then serves typed requests through registered handlers only.
/// </summary>
public sealed class WorkerHost
{
    private readonly Dictionary<string, Func<JsonElement, object?>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string op, Func<JsonElement, object?> handler) => _handlers[op] = handler;

    public void Register<TReq>(string op, Func<TReq, object?> handler) =>
        _handlers[op] = payload => handler(payload.Deserialize<TReq>(Json.Canonical)!);

    public int Run(string pipeName, int parentPid)
    {
        Log.Process = "worker";
        Log.Info("Worker", $"Starting (elevated={AppInfo.IsElevated}) for parent {parentPid}");
        if (!AppInfo.IsElevated) { Log.Error("Worker", "Worker is not elevated; exiting"); return 2; }

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid);
            var parentPath = parent.MainModule?.FileName;
            if (!string.Equals(Path.GetFullPath(parentPath ?? ""), Path.GetFullPath(AppInfo.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("Worker", $"Parent executable mismatch: {parentPath}");
                return 3;
            }
        }
        catch (Exception ex) { Log.Error("Worker", "Parent process not found", ex); return 3; }

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { pipe.Connect(10_000); }
        catch (Exception ex) { Log.Error("Worker", "Cannot connect to UI pipe", ex); return 4; }

        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) || serverPid != (uint)parentPid)
        {
            Log.Error("Worker", $"Pipe server pid {serverPid} is not the parent {parentPid}");
            return 5;
        }

        using var cts = new CancellationTokenSource();
        parent.EnableRaisingEvents = true;
        parent.Exited += (_, _) => cts.Cancel();

        while (!cts.IsCancellationRequested)
        {
            PipeRequest? request;
            try { request = PipeProtocol.ReadAsync<PipeRequest>(pipe, cts.Token).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or EndOfStreamException) { break; }
            if (request == null) break;
            if (request.Op == "shutdown") break;

            var response = new PipeResponse { Id = request.Id };
            try
            {
                WindowsRegistryAccess.HkcuSidOverride = request.UserSid;
                if (!_handlers.TryGetValue(request.Op, out var handler))
                    throw new UnauthorizedAccessException($"Operation not allowed: {request.Op}");
                var result = handler(request.Payload);
                response.Ok = true;
                response.Payload = PipeProtocol.ToElement(result);
            }
            catch (Exception ex)
            {
                Log.Error("Worker", $"{PipeProtocol.Describe(request)} failed", ex);
                response.Ok = false;
                response.Error = ex.Message;
                response.ErrorType = ex.GetType().Name;
            }
            finally { WindowsRegistryAccess.HkcuSidOverride = null; }

            try { PipeProtocol.WriteAsync(pipe, response, cts.Token).GetAwaiter().GetResult(); }
            catch { break; }
        }
        Log.Info("Worker", "Exiting");
        return 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
}
